using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Retune.Core.Net;
using Retune.Core.Policies;
using Retune.Infrastructure.Storage;

namespace Retune.Infrastructure.Net;

/// <summary>Fetches poster and logo bytes for the UI, which decodes and downsizes them.</summary>
public interface IArtworkSource
{
    /// <summary>The image bytes, or null when there is none (missing, refused, or not an image).</summary>
    Task<byte[]?> GetAsync(Uri url, CancellationToken cancellationToken = default);

    /// <summary>The bytes didn't decode: never ask for this URL again this session.</summary>
    void MarkUndecodable(Uri url);

    /// <summary>Settings › "Cached posters and logos" › Clear.</summary>
    void Clear();
}

/// <summary>
/// A content-addressed disk cache (SHA-256 of the URL) with in-flight de-duplication, a session-long negative
/// cache for 404/410/undecodable URLs, and least-recently-used eviction above a size limit. Timeouts and
/// server errors are not remembered, so they are retried next time. Spec: docs/spec/ui-player.md §5.
/// </summary>
public sealed class ArtworkCache : IArtworkSource
{
    public const long DefaultMaxBytes = 1L << 30;

    /// <summary>Evict down to this fraction of the limit, so trimming doesn't run on every write.</summary>
    private const double TrimTarget = 0.9;

    private readonly string _directory;
    private readonly IHttpFetcher _fetcher;
    private readonly long _maxBytes;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _negative = new(StringComparer.Ordinal);
    private readonly Lock _trimGate = new();
    private long _approximateSize = -1;

    public ArtworkCache(string directory, IHttpFetcher fetcher, long maxBytes = DefaultMaxBytes, TimeProvider? time = null)
    {
        _directory = directory;
        _fetcher = fetcher;
        _maxBytes = maxBytes;
        _time = time ?? TimeProvider.System;
        PrivateFolder.Ensure(directory);
    }

    public Task<byte[]?> GetAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!UrlPolicy.IsAllowedArtwork(url))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var key = Key(url);
        if (_negative.ContainsKey(key))
        {
            return Task.FromResult<byte[]?>(null);
        }

        // Register the shared task before starting the work, so a load that completes synchronously can't leave a
        // stale entry behind. Shared work must not be cancelled by whichever caller happened to start it.
        var owner = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = _inFlight.GetOrAdd(key, owner.Task);
        if (shared == owner.Task)
        {
            _ = RunAsync(url, key, owner);
        }

        return shared.WaitAsync(cancellationToken);
    }

    private async Task RunAsync(Uri url, string key, TaskCompletionSource<byte[]?> owner)
    {
        byte[]? result = null;
        try
        {
            result = await LoadAsync(url, key).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Task<byte[]?>>(key, owner.Task));
            owner.TrySetResult(result);
        }
    }

    public void MarkUndecodable(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var key = Key(url);
        _negative[key] = 0;
        TryDelete(PathFor(key));
    }

    public void Clear()
    {
        _negative.Clear();
        foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
        {
            TryDelete(file);
        }

        Interlocked.Exchange(ref _approximateSize, 0);
    }

    private async Task<byte[]?> LoadAsync(Uri url, string key)
    {
        try
        {
            var path = PathFor(key);
            if (File.Exists(path))
            {
                Touch(path);
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            }

            HttpBody response;
            try
            {
                response = await _fetcher.GetAsync(url.AbsoluteUri, FetchLimits.MaxArtworkBytes, CancellationToken.None).ConfigureAwait(false);
            }
            catch (FetchException ex)
            {
                if (ex.Failure == FetchFailure.TooLarge)
                {
                    _negative[key] = 0;
                }

                return null;
            }

            switch (response.StatusCode ?? 200)
            {
                case 200 when !response.Content.IsEmpty:
                    var bytes = response.Content.ToArray();
                    Store(path, bytes);
                    return bytes;
                case 200 or 404 or 410:
                    _negative[key] = 0;
                    return null;
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // a disk problem must never break browsing; the next request tries again
        }
    }

    private void Store(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
        Touch(path);

        if (Interlocked.Read(ref _approximateSize) < 0)
        {
            Interlocked.Exchange(ref _approximateSize, MeasureSize());
        }

        if (Interlocked.Add(ref _approximateSize, bytes.Length) > _maxBytes)
        {
            _ = Task.Run(Trim);
        }
    }

    /// <summary>Deletes the least recently used files until the cache is back under 90% of the limit.</summary>
    internal void Trim()
    {
        lock (_trimGate)
        {
            var files = new DirectoryInfo(_directory).EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => !f.Name.EndsWith(".tmp", StringComparison.Ordinal))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            var total = files.Sum(f => f.Length);
            var target = (long)(_maxBytes * TrimTarget);
            foreach (var file in files)
            {
                if (total <= target)
                {
                    break;
                }

                total -= file.Length;
                TryDelete(file.FullName);
            }

            Interlocked.Exchange(ref _approximateSize, total);
        }
    }

    private long MeasureSize() =>
        new DirectoryInfo(_directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    /// <summary>Marks a hit as recently used (NTFS last-access updates are often disabled, so use the write time).</summary>
    private void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
        }
        catch (IOException)
        {
            // Another reader has it open; recency is best effort.
        }
    }

    private static string Key(Uri url) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)));

    /// <summary>Two-character shard folders keep any one directory small.</summary>
    private string PathFor(string key) => Path.Combine(_directory, key[..2], key);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use; it is retried by the next trim or clear.
        }
    }
}
