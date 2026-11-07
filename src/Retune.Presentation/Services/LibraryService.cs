using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Parental;
using Retune.Core.Persistence;
using Retune.Core.Xtream;

namespace Retune.Presentation.Services;

/// <summary>
/// The one live copy of the library. Every change goes through <see cref="Update"/>, is saved 1 s later
/// (coalesced) and announced via <see cref="Changed"/>. Also holds the session-only PIN unlocks.
/// UI thread only.
/// </summary>
public sealed class LibraryService : IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly DebouncedWriter<LibraryState> _writer;
    private readonly HashSet<string> _unlocked = new(StringComparer.Ordinal);
    private readonly ProgressThrottle _throttle;
    private readonly TimeProvider _time;

    public LibraryService(ILibraryStore store, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        _time = time;
        _throttle = new ProgressThrottle(time);
        Pins = new PinAttemptLimiter(time);
        _writer = new DebouncedWriter<LibraryState>(store.Save, SaveDelay, time);

        var (state, changed) = store.Load().SanitizeStreamUrls();
        State = state;
        if (changed)
        {
            store.Save(state);
        }
    }

    public event EventHandler? Changed;

    public LibraryState State { get; private set; }

    public IReadOnlySet<string> UnlockedCategories => _unlocked;

    public PinAttemptLimiter Pins { get; }

    public bool HasUnlocks => _unlocked.Count > 0;

    public void Update(Func<LibraryState, LibraryState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = change(State);
        if (ReferenceEquals(next, State))
        {
            return;
        }

        State = next;
        _writer.Schedule(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Unlock(string categoryKey)
    {
        if (_unlocked.Add(categoryKey))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>"Lock Categories Again" (Ctrl+L).</summary>
    public void LockAll()
    {
        if (_unlocked.Count > 0)
        {
            _unlocked.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removing a PIN also drops session unlocks.</summary>
    public void ForgetUnlocks() => _unlocked.Clear();

    /// <summary>Checks a PIN, upgrading a legacy hash on success, and applies the attempt limiter.</summary>
    public PinCheck CheckPin(string pin)
    {
        if (!Pins.CanAttempt)
        {
            return PinCheck.Wrong;
        }

        var result = PinHash.Verify(pin, State.PinHash);
        switch (result)
        {
            case PinCheck.CorrectLegacy:
                Update(s => s.WithPinHash(PinHash.Create(pin)));
                Pins.RecordSuccess();
                break;
            case PinCheck.Correct:
                Pins.RecordSuccess();
                break;
            default:
                Pins.RecordFailure();
                break;
        }

        return result;
    }

    /// <summary>
    /// Stores progress for an on-demand item: throttled to once per 30 s unless final, made credential-free,
    /// and never for live items. Spec: docs/spec/core.md §5.3.
    /// </summary>
    public void RecordProgress(Guid playlistId, PlaybackItem item, double position, double duration, bool isFinal, bool hasAccount)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsLive)
        {
            return;
        }

        var key = LibraryKeys.For(playlistId, item);
        if (!_throttle.ShouldCommit(key, isFinal) || Locators.CredentialFree(item, hasAccount) is not { } stored)
        {
            return;
        }

        Update(s => s.RecordProgress(key, stored, position, duration, _time.GetUtcNow()));
    }

    public void RecordRecentChannel(Guid playlistId, PlaybackItem item) =>
        Update(s => s.RecordRecentChannel(LibraryKeys.For(playlistId, item)));

    /// <summary>Writes any pending change now (on quit and before deleting a playlist).</summary>
    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();

    public static string Key(Guid playlistId, ContentKind kind, string id) => LibraryKeys.Content(playlistId, kind, id);
}
