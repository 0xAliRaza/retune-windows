using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Retune.Infrastructure.Storage;

/// <summary>
/// Crash-safe JSON files in one private folder. Spec: docs/spec/core.md §8.2.
/// <list type="bullet">
/// <item>Missing file → null (callers use defaults).</item>
/// <item>Unreadable file → renamed to <c>name.corrupt-&lt;unix seconds&gt;</c>, then null, so the next save can't destroy it.</item>
/// <item>Save → write a temp file in the same folder, flush to disk, then atomically replace the target.</item>
/// </list>
/// </summary>
public sealed class JsonFileStore
{
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    private readonly bool _encrypt;

    /// <param name="encrypt">Encrypt files at rest with DPAPI (for files that can hold provider credentials).</param>
    public JsonFileStore(string directory, TimeProvider? time = null, ILogger? logger = null, bool encrypt = false)
    {
        _directory = directory;
        _encrypt = encrypt;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        PrivateFolder.Ensure(directory);
    }

    public string PathOf(string name) => Path.Combine(_directory, name);

    public T? Load<T>(string name, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return Load(name, bytes => JsonSerializer.Deserialize(bytes, typeInfo));
    }

    /// <summary>Loads with a custom reader; any <see cref="JsonException"/> or <see cref="FormatException"/> counts as corrupt.</summary>
    public T? Load<T>(string name, Func<byte[], T?> read)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(read);
        var path = PathOf(name);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Held by another program (backup, antivirus) or unreadable: start without it, leave the file alone.
            _logger.LogWarning(ex, "{File} couldn't be read now; using defaults.", name);
            return null;
        }

        try
        {
            return read(AtRestProtection.Unprotect(bytes)) ?? throw new JsonException("The file contained JSON null.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException or InvalidOperationException)
        {
            var backup = $"{path}.corrupt-{_time.GetUtcNow().ToUnixTimeSeconds()}";
            try
            {
                File.Move(path, backup, overwrite: true);
                _logger.LogWarning(ex, "{File} was unreadable and was moved aside as {Backup}.", name, Path.GetFileName(backup));
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(moveError, "{File} was unreadable and couldn't be moved aside.", name);
            }

            return null;
        }
    }

    public void Save<T>(string name, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        Save(name, JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
    }

    public void Save(string name, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (_encrypt)
        {
            bytes = AtRestProtection.Protect(bytes);
        }

        var target = PathOf(name);
        var temp = Path.Combine(_directory, $".{name}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public void Delete(string name) => TryDelete(PathOf(name));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp or cache file is harmless and is replaced on the next write.
        }
    }
}
