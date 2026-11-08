using Retune.Core.Diagnostics;
using Retune.Core.Models;
using Retune.Core.Persistence;

namespace Retune.Presentation.Services;

/// <summary>
/// The one in-memory copy of <see cref="AppSettings"/>. Every writer changes it through <see cref="Update"/>, so
/// two features never overwrite each other's fields with stale snapshots. Rapid changes (volume slider, subtitle
/// style) are written debounced; everything else at once. UI thread only.
/// </summary>
public sealed class SettingsService : IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IAppLog _log;
    private readonly DebouncedWriter<AppSettings> _writer;

    public SettingsService(ISettingsStore store, TimeProvider time, IAppLog? log = null)
    {
        _log = log ?? NullAppLog.Instance;
        ArgumentNullException.ThrowIfNull(store);
        Current = store.Load();
        _writer = new DebouncedWriter<AppSettings>(store.Save, SaveDelay, time, ex => _log.Warning("settings save", "Couldn't save; will retry.", ex));
    }

    /// <summary>Raised after every change, with the new settings already in <see cref="Current"/>.</summary>
    public event EventHandler? Changed;

    public AppSettings Current { get; private set; }

    public void Update(Func<AppSettings, AppSettings> change, bool debounce = false)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = change(Current) ?? throw new InvalidOperationException("A settings change must return settings.");
        if (next == Current)
        {
            return;
        }

        Current = next;
        _writer.Schedule(next);
        if (!debounce)
        {
            _writer.Flush();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes a pending debounced change now (app exit).</summary>
    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}
