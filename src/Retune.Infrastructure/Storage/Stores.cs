using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Persistence;
using Retune.Core.Playback;

namespace Retune.Infrastructure.Storage;

/// <summary>
/// playlists.json. Give it an encrypting <see cref="JsonFileStore"/>: M3U links usually carry the username and password.
/// </summary>
public sealed class PlaylistStore(JsonFileStore files) : IPlaylistStore
{
    internal const string FileName = "playlists.json";

    public IReadOnlyList<Playlist> Load() =>
        files.Load(FileName, RetuneJson.TypeInfo<PlaylistsFile>())?.Playlists ?? [];

    public void Save(IReadOnlyList<Playlist> playlists) =>
        files.Save(FileName, new PlaylistsFile(PlaylistsFile.CurrentVersion, playlists), RetuneJson.TypeInfo<PlaylistsFile>());
}

public sealed class LibraryStore(JsonFileStore files) : ILibraryStore
{
    internal const string FileName = "library.json";

    public LibraryState Load() => files.Load(FileName, Decode) ?? LibraryState.Empty;

    public void Save(LibraryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var progress = state.Progress.ToDictionary(
            p => p.Key,
            p => JsonSerializer.SerializeToElement(p.Value, RetuneJson.TypeInfo<WatchProgress>()),
            StringComparer.Ordinal);
        var file = new LibraryFile(
            LibraryFile.CurrentVersion,
            [.. state.Favorites.Order(StringComparer.Ordinal)],
            progress,
            state.RecentChannels,
            [.. state.HiddenCategories.Order(StringComparer.Ordinal)],
            [.. state.LockedCategories.Order(StringComparer.Ordinal)],
            state.PinnedCategories,
            state.PinHash);
        files.Save(FileName, file, RetuneJson.TypeInfo<LibraryFile>());
    }

    private static LibraryState Decode(byte[] bytes)
    {
        var file = JsonSerializer.Deserialize(bytes, RetuneJson.TypeInfo<LibraryFile>()) ?? throw new JsonException("Empty library.");
        var progress = ImmutableDictionary.CreateBuilder<string, WatchProgress>(StringComparer.Ordinal);
        foreach (var (key, element) in file.Progress ?? ImmutableDictionary<string, JsonElement>.Empty)
        {
            try
            {
                if (element.Deserialize(RetuneJson.TypeInfo<WatchProgress>()) is { } entry && IsComplete(entry))
                {
                    progress[key] = entry;
                }
            }
            catch (JsonException)
            {
                // One malformed entry (e.g. an unknown kind) is skipped; its neighbours survive.
            }
        }

        // Missing required members deserialise as null (optional nulls are not written), so check them here.
        static bool IsComplete(WatchProgress p) =>
            p.Item is { Id: not null, Title: not null, Url: not null } && double.IsFinite(p.Position) && double.IsFinite(p.Duration);

        return new LibraryState
        {
            Favorites = [.. file.Favorites ?? []],
            Progress = progress.ToImmutable(),
            RecentChannels = [.. file.RecentChannels ?? []],
            HiddenCategories = [.. file.HiddenCategories ?? []],
            LockedCategories = [.. file.LockedCategories ?? []],
            PinnedCategories = [.. file.PinnedCategories ?? []],
            PinHash = file.PinHash,
        };
    }
}

/// <summary>
/// One JSON file per playlist. Saves and deletes are queued and run strictly in order on a background
/// thread, so deleting a playlist's cache can never be undone by a save that was still running.
/// </summary>
public sealed class CatalogCache : ICatalogCache, IDisposable
{
    private readonly JsonFileStore _files;
    private readonly SemaphoreSlim _queue = new(1, 1);

    /// <summary>Catalog files are encrypted at rest: M3U catalogs hold stream URLs with credentials in them.</summary>
    public CatalogCache(string directory, TimeProvider? time = null)
    {
        _files = new JsonFileStore(directory, time, encrypt: true);
    }

    public async Task<Catalog?> LoadAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => _files.Load(FileName(playlistId), RetuneJson.TypeInfo<CatalogFile>())?.Catalog, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _queue.Release();
        }
    }

    public Task SaveAsync(Guid playlistId, Catalog catalog) =>
        Enqueue(() => _files.Save(FileName(playlistId), new CatalogFile(CatalogFile.CurrentVersion, catalog), RetuneJson.TypeInfo<CatalogFile>()));

    public Task DeleteAsync(Guid playlistId) => Enqueue(() => _files.Delete(FileName(playlistId)));

    public void Dispose() => _queue.Dispose();

    private static string FileName(Guid playlistId) => $"{playlistId:D}.json";

    private async Task Enqueue(Action work)
    {
        await _queue.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            _queue.Release();
        }
    }
}

/// <summary>
/// settings.json, read key by key so one bad or unknown value falls back to its default
/// instead of resetting every preference.
/// </summary>
public sealed class SettingsStore(JsonFileStore files) : ISettingsStore
{
    internal const string FileName = "settings.json";

    public AppSettings Load() => files.Load(FileName, Decode) ?? AppSettings.Default;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            if (settings.ActivePlaylist is { } active)
            {
                writer.WriteString("activePlaylist", active);
            }

            writer.WriteString("layout.live", settings.LiveLayout.ToString());
            writer.WriteString("layout.movies", settings.MoviesLayout.ToString());
            writer.WriteString("layout.series", settings.SeriesLayout.ToString());
            writer.WriteString("layout.tileSize", settings.TileSize.ToString());
            writer.WriteNumber("ui.zoom", ZoomLevel.Clamped(settings.Zoom));
            writer.WriteBoolean("ui.sidebarCollapsed", settings.SidebarCollapsed);
            if (settings.Window is { } w && double.IsFinite(w.Left) && double.IsFinite(w.Top) && double.IsFinite(w.Width) && double.IsFinite(w.Height))
            {
                writer.WriteStartObject("ui.window");
                writer.WriteNumber("left", w.Left);
                writer.WriteNumber("top", w.Top);
                writer.WriteNumber("width", w.Width);
                writer.WriteNumber("height", w.Height);
                writer.WriteBoolean("maximized", w.Maximized);
                writer.WriteEndObject();
            }
            writer.WriteString("player.liveFormat", settings.LiveFormat.ToString());
            writer.WriteBoolean("player.autoplayNext", settings.AutoplayNext);
            writer.WriteBoolean("player.allowUnverifiedHttps", settings.AllowUnverifiedHttps);
            writer.WriteNumber("player.volume", VolumeLevel.Clamped(settings.Volume));
            writer.WritePropertyName("player.subtitleStyle");
            JsonSerializer.Serialize(writer, settings.SubtitleStyle.Clamped(), RetuneJson.TypeInfo<SubtitleStyle>());
            writer.WriteEndObject();
        }

        files.Save(FileName, stream.ToArray());
    }

    private static AppSettings Decode(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Settings must be an object.");
        }

        var d = AppSettings.Default;
        return new AppSettings
        {
            ActivePlaylist = Text(root, "activePlaylist") is { } id && Guid.TryParse(id, out var guid) ? guid : null,
            LiveLayout = EnumOr(root, "layout.live", d.LiveLayout),
            MoviesLayout = EnumOr(root, "layout.movies", d.MoviesLayout),
            SeriesLayout = EnumOr(root, "layout.series", d.SeriesLayout),
            TileSize = EnumOr(root, "layout.tileSize", d.TileSize),
            LiveFormat = EnumOr(root, "player.liveFormat", d.LiveFormat),
            AutoplayNext = root.TryGetProperty("player.autoplayNext", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False ? a.GetBoolean() : d.AutoplayNext,
            Volume = root.TryGetProperty("player.volume", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var vol) ? VolumeLevel.Clamped(vol) : d.Volume,
            SubtitleStyle = SubtitleOr(root, d.SubtitleStyle),
            AllowUnverifiedHttps = root.TryGetProperty("player.allowUnverifiedHttps", out var tls) && tls.ValueKind == JsonValueKind.True,
            SidebarCollapsed = root.TryGetProperty("ui.sidebarCollapsed", out var sc) && sc.ValueKind == JsonValueKind.True,
            Window = WindowOr(root),
            Zoom = root.TryGetProperty("ui.zoom", out var z) && z.ValueKind == JsonValueKind.Number && z.TryGetDouble(out var zoom) ? ZoomLevel.Clamped(zoom) : d.Zoom,
        };
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static T EnumOr<T>(JsonElement root, string name, T fallback)
        where T : struct, Enum =>
        Text(root, name) is { } text && Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? value
            : fallback;

    private static WindowPlacement? WindowOr(JsonElement root)
    {
        if (!root.TryGetProperty("ui.window", out var w) || w.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        double? Number(string name) => w.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetDouble(out var v) && double.IsFinite(v) ? v : null;
        return Number("left") is { } left && Number("top") is { } top && Number("width") is { } width && Number("height") is { } height
            ? new WindowPlacement(left, top, width, height, w.TryGetProperty("maximized", out var m) && m.ValueKind == JsonValueKind.True)
            : null;
    }

    private static SubtitleStyle SubtitleOr(JsonElement root, SubtitleStyle fallback)
    {
        if (!root.TryGetProperty("player.subtitleStyle", out var element))
        {
            return fallback;
        }

        try
        {
            return element.Deserialize(RetuneJson.TypeInfo<SubtitleStyle>())?.Clamped() ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}

/// <summary>Reads a local M3U file with the same size cap as downloads.</summary>
public sealed class LocalFileReader : ILocalFileReader
{
    public async Task<byte[]> ReadAsync(string path, long maxBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > maxBytes)
            {
                throw Core.Errors.RetuneException.ResponseTooLarge();
            }

            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Core.Errors.RetuneException.Network("The playlist file couldn't be found. It may have been moved or deleted.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Core.Errors.RetuneException.Network("The playlist file couldn't be read.", ex);
        }
    }
}
