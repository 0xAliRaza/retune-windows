using Retune.Core.Library;
using Retune.Core.Models;

namespace Retune.Core.Persistence;

/// <summary>The configured playlists. Saved immediately on every add, edit and delete.</summary>
public interface IPlaylistStore
{
    IReadOnlyList<Playlist> Load();

    void Save(IReadOnlyList<Playlist> playlists);
}

/// <summary>The library document. A missing or unreadable file loads as <see cref="LibraryState.Empty"/>.</summary>
public interface ILibraryStore
{
    LibraryState Load();

    void Save(LibraryState state);
}

/// <summary>Cached catalogs per playlist. Saves and deletes run strictly in the order they were requested.</summary>
public interface ICatalogCache
{
    Task<Catalog?> LoadAsync(Guid playlistId, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid playlistId, Catalog catalog);

    Task DeleteAsync(Guid playlistId);
}

/// <summary>Xtream passwords, kept in the operating system's secret store (never in plain files).</summary>
public interface ISecretStore
{
    /// <summary>Null when missing or access is denied.</summary>
    string? Find(Guid playlistId);

    /// <exception cref="Errors.RetuneException">When the secret can't be stored.</exception>
    void Store(Guid playlistId, string secret);

    void Remove(Guid playlistId);
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>Reads a local playlist file with a size cap.</summary>
public interface ILocalFileReader
{
    /// <exception cref="Errors.RetuneException">When the file is missing, unreadable or too large.</exception>
    Task<byte[]> ReadAsync(string path, long maxBytes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Coalesces rapid changes into one save: each <see cref="Schedule"/> replaces the pending snapshot and
/// restarts the delay. <see cref="Flush"/> writes the pending snapshot now (on quit). Writes never overlap, so an
/// older snapshot can't land after a newer one, and a failed save (disk full, file held by another program) is
/// reported to <paramref name="onError"/> and kept pending for the next attempt instead of crashing the app.
/// </summary>
public sealed class DebouncedWriter<T>(Action<T> write, TimeSpan delay, TimeProvider time, Action<Exception>? onError = null) : IDisposable
    where T : class
{
    private readonly Lock _gate = new();
    private readonly Lock _writing = new();
    private T? _pending;
    private ITimer? _timer;

    public void Schedule(T snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _pending = snapshot;
            _timer?.Dispose();
            _timer = time.CreateTimer(_ => Flush(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush()
    {
        // Held across the write: a Flush on quit waits for a timer write already in progress, then writes the newest.
        lock (_writing)
        {
            T? snapshot;
            lock (_gate)
            {
                snapshot = _pending;
                _pending = null;
                _timer?.Dispose();
                _timer = null;
            }

            if (snapshot is null)
            {
                return;
            }

            try
            {
                write(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_gate)
                {
                    _pending ??= snapshot; // retried with the next change or the final flush
                }

                onError?.Invoke(ex);
            }
        }
    }

    public void Dispose() => Flush();
}
