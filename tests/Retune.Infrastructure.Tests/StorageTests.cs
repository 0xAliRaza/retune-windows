using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Persistence;
using Retune.Core.Playback;
using Retune.Infrastructure.Storage;
using Retune.Infrastructure.Tests.Support;

namespace Retune.Infrastructure.Tests;

public sealed class StorageTests : IDisposable
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static readonly Guid P = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(T0);

    public void Dispose() => _temp.Dispose();

    private JsonFileStore Files() => new(_temp.Path, _time);

    [Fact]
    public void The_data_folder_is_private_to_the_current_user()
    {
        Files();

        Assert.True(PrivateFolder.IsPrivate(_temp.Path));
    }

    [Fact]
    public void Playlists_round_trip_with_both_source_kinds()
    {
        var store = new PlaylistStore(Files());
        Playlist[] playlists =
        [
            new(P, "Home", new XtreamSource("http://h.example:8080", "viewer"), T0),
            new(Guid.NewGuid(), "List", new M3uSource(@"C:\Users\Ali Raza\Downloads\tv list.m3u"), T0),
        ];

        store.Save(playlists);
        var loaded = store.Load();

        Assert.Equal(playlists, loaded);
        var json = File.ReadAllText(_temp.File(PlaylistStore.FileName));
        Assert.Contains("\"$type\":\"xtream\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("kindLabel", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Encrypted_stores_hide_credentials_on_disk_and_still_read_old_plain_files()
    {
        var plainStore = new PlaylistStore(Files());
        var link = new Playlist(P, "Link", new M3uSource("http://h.example/get.php?username=viewer&password=topsecret&type=m3u"), T0);
        plainStore.Save([link]);

        var encrypted = new PlaylistStore(new JsonFileStore(_temp.Path, _time, encrypt: true));
        Assert.Equal([link], encrypted.Load());

        encrypted.Save([link]);
        var raw = File.ReadAllBytes(_temp.File(PlaylistStore.FileName));
        Assert.True(AtRestProtection.IsProtected(raw));
        Assert.DoesNotContain("topsecret", Encoding.Latin1.GetString(raw), StringComparison.Ordinal);
        Assert.Equal([link], encrypted.Load());
    }

    [Fact]
    public void A_file_encrypted_for_someone_else_is_moved_aside_not_trusted()
    {
        File.WriteAllBytes(_temp.File(PlaylistStore.FileName), [.. "RTN1"u8, 1, 2, 3, 4, 5]);

        Assert.Empty(new PlaylistStore(new JsonFileStore(_temp.Path, _time, encrypt: true)).Load());
        Assert.Single(Directory.GetFiles(_temp.Path, "playlists.json.corrupt-*"));
    }

    [Fact]
    public async Task Catalog_files_are_encrypted_at_rest()
    {
        using var cache = new CatalogCache(_temp.Path, _time);
        var catalog = new Catalog(T0, [], [], [], [new Channel("1", 1, "C", null, null, null, "http://h/live/user/topsecret/1.ts", false)], [], []);

        await cache.SaveAsync(P, catalog);

        var raw = File.ReadAllBytes(Directory.GetFiles(_temp.Path, "*.json").Single());
        Assert.DoesNotContain("topsecret", Encoding.Latin1.GetString(raw), StringComparison.Ordinal);
        Assert.Equal(catalog.Channels, (await cache.LoadAsync(P, TestContext.Current.CancellationToken))!.Channels);
    }

    [Fact]
    public void A_missing_file_loads_defaults()
    {
        Assert.Empty(new PlaylistStore(Files()).Load());
        Assert.Equal(LibraryState.Empty, new LibraryStore(Files()).Load());
        Assert.Equal(AppSettings.Default, new SettingsStore(Files()).Load());
    }

    [Fact]
    public void Zoom_is_saved_and_a_bad_value_falls_back()
    {
        var store = new SettingsStore(Files());
        store.Save(AppSettings.Default with { Zoom = 1.25 });
        Assert.Equal(1.25, store.Load().Zoom);

        File.WriteAllText(_temp.File(SettingsStore.FileName), "{ \"version\": 1, \"ui.zoom\": 40 }");
        Assert.Equal(ZoomLevel.Max, new SettingsStore(Files()).Load().Zoom);

        File.WriteAllText(_temp.File(SettingsStore.FileName), "{ \"version\": 1, \"ui.zoom\": \"big\" }");
        Assert.Equal(ZoomLevel.Default, new SettingsStore(Files()).Load().Zoom);
    }

    [Fact]
    public void A_corrupt_store_is_backed_up_not_overwritten()
    {
        File.WriteAllText(_temp.File(LibraryStore.FileName), "{ this is not json");
        var store = new LibraryStore(Files());

        Assert.Equal(LibraryState.Empty, store.Load());
        var backup = Assert.Single(Directory.GetFiles(_temp.Path, "library.json.corrupt-*"));
        Assert.EndsWith($".corrupt-{T0.ToUnixTimeSeconds()}", backup, StringComparison.Ordinal);

        store.Save(LibraryState.Empty.ToggleFavorite("x"));
        Assert.Contains("x", store.Load().Favorites);
        Assert.Equal("{ this is not json", File.ReadAllText(backup));
    }

    [Fact]
    public void Library_round_trips_every_field()
    {
        var store = new LibraryStore(Files());
        var item = new PlaybackItem("e1", ContentKind.Episode, "Show", "S1 E1 · Pilot", "retune://series/e1.mkv", new Uri("http://img.example/e.jpg"), ParentId: "s1");
        var state = LibraryState.Empty
            .ToggleFavorite("f")
            .RecordProgress("k", item, 600, 3000, T0)
            .RecordRecentChannel("r1")
            .RecordRecentChannel("r2")
            .ToggleHidden("h")
            .SetLocked("l", true)
            .TogglePinned("p2")
            .TogglePinned("p1")
            .WithPinHash("pbkdf2-sha256$310000$aa$bb");

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(state.Favorites, loaded.Favorites);
        Assert.Equal(state.Progress["k"], loaded.Progress["k"]);
        Assert.Equal(["r2", "r1"], loaded.RecentChannels);
        Assert.Equal(["p2", "p1"], loaded.PinnedCategories);
        Assert.Contains("h", loaded.HiddenCategories);
        Assert.Contains("l", loaded.LockedCategories);
        Assert.Equal(state.PinHash, loaded.PinHash);
        Assert.DoesNotContain("isResumable", File.ReadAllText(_temp.File(LibraryStore.FileName)), StringComparison.Ordinal);
    }

    [Fact]
    public void Library_decoding_skips_one_bad_progress_entry()
    {
        File.WriteAllText(_temp.File(LibraryStore.FileName), """
            {"version":1,"favorites":["a"],"progress":{
              "bad":{"item":{"id":"1","kind":"Hologram","title":"T","url":"retune://movie/1.mp4"},"position":1,"duration":2,"updatedAt":"2024-01-01T00:00:00+00:00"},
              "good":{"item":{"id":"2","kind":"Movie","title":"T","url":"retune://movie/2.mp4"},"position":600,"duration":6000,"updatedAt":"2024-01-01T00:00:00+00:00"},
              "noItem":{"position":1,"duration":2,"updatedAt":"2024-01-01T00:00:00+00:00"}
            }}
            """);

        var loaded = new LibraryStore(Files()).Load();

        Assert.Equal(["good"], loaded.Progress.Keys);
        Assert.Equal(["a"], loaded.Favorites);
    }

    [Fact]
    public void Library_with_a_type_error_outside_progress_is_corrupt()
    {
        File.WriteAllText(_temp.File(LibraryStore.FileName), """{"version":1,"favorites":"not a list"}""");

        var loaded = new LibraryStore(Files()).Load();

        Assert.Empty(loaded.Favorites);
        Assert.Single(Directory.GetFiles(_temp.Path, "library.json.corrupt-*"));
    }

    [Fact]
    public void Legacy_library_without_newer_fields_decodes_with_defaults()
    {
        File.WriteAllText(_temp.File(LibraryStore.FileName), """{"version":1,"favorites":["a"],"progress":{},"recentChannels":["r"]}""");

        var loaded = new LibraryStore(Files()).Load();

        Assert.Equal(["a"], loaded.Favorites);
        Assert.Equal(["r"], loaded.RecentChannels);
        Assert.Empty(loaded.PinnedCategories);
        Assert.Null(loaded.PinHash);
    }

    [Fact]
    public void Settings_round_trip_and_bad_values_fall_back_one_by_one()
    {
        var store = new SettingsStore(Files());
        var settings = AppSettings.Default
            .WithLayout(LibrarySection.Movies, LayoutMode.Compact) with
        {
            ActivePlaylist = P,
            TileSize = TileSize.Large,
            LiveFormat = LiveFormat.Hls,
            AutoplayNext = false,
            Volume = 42,
            SubtitleStyle = new SubtitleStyle(1.4, 80, Background: true),
        };

        store.Save(settings);
        Assert.Equal(settings, store.Load());

        File.WriteAllText(_temp.File(SettingsStore.FileName), """
            {"layout.live":"Holographic","layout.movies":"Grid","layout.tileSize":"7","player.volume":"loud",
             "player.subtitleStyle":{"scale":9,"position":10},"player.autoplayNext":"yes","activePlaylist":"nope"}
            """);
        var tolerant = store.Load();
        Assert.Equal(LayoutMode.List, tolerant.LiveLayout);
        Assert.Equal(LayoutMode.Grid, tolerant.MoviesLayout);
        Assert.Equal(TileSize.Medium, tolerant.TileSize);
        Assert.Equal(100, tolerant.Volume);
        Assert.Equal(2.5, tolerant.SubtitleStyle.Scale);
        Assert.Equal(50, tolerant.SubtitleStyle.Position);
        Assert.True(tolerant.AutoplayNext);
        Assert.Null(tolerant.ActivePlaylist);
    }

    [Fact]
    public async Task Catalog_cache_round_trips_and_deletes_run_after_pending_saves()
    {
        using var cache = new CatalogCache(_temp.Path, _time);
        var catalog = SampleCatalog(3);

        var save = cache.SaveAsync(P, catalog);
        var delete = cache.DeleteAsync(P);
        await Task.WhenAll(save, delete);
        Assert.Null(await cache.LoadAsync(P, TestContext.Current.CancellationToken));

        await cache.SaveAsync(P, catalog);
        var loaded = await cache.LoadAsync(P, TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(catalog.Channels, loaded.Channels);
        Assert.Equal(catalog.Movies, loaded.Movies);
        Assert.Equal(catalog.Series, loaded.Series);
        Assert.Equal(catalog.FetchedAt, loaded.FetchedAt);
    }

    [Fact]
    public async Task An_88k_catalog_saves_and_loads_in_reasonable_time()
    {
        using var cache = new CatalogCache(_temp.Path, _time);
        var catalog = SampleCatalog(88_000);

        var save = Stopwatch.StartNew();
        await cache.SaveAsync(P, catalog);
        save.Stop();
        var load = Stopwatch.StartNew();
        var loaded = await cache.LoadAsync(P, TestContext.Current.CancellationToken);
        load.Stop();

        Assert.Equal(catalog.Movies.Count, loaded!.Movies.Count);
        Assert.True(load.Elapsed < TimeSpan.FromSeconds(3), $"Load took {load.Elapsed}; save took {save.Elapsed}.");
    }

    [Fact]
    public void Debounced_writer_coalesces_changes_and_flushes_on_demand()
    {
        var writes = new List<string>();
        using var writer = new DebouncedWriter<string>(writes.Add, TimeSpan.FromSeconds(1), _time);

        writer.Schedule("a");
        _time.Advance(TimeSpan.FromMilliseconds(900));
        writer.Schedule("b");
        _time.Advance(TimeSpan.FromMilliseconds(900));
        Assert.Empty(writes);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(["b"], writes);

        writer.Schedule("c");
        writer.Flush();
        Assert.Equal(["b", "c"], writes);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(["b", "c"], writes);
    }

    [Fact]
    public async Task Local_file_reader_caps_the_size()
    {
        var path = _temp.File("big list.m3u");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(new string('x', 2048)), TestContext.Current.CancellationToken);
        var reader = new LocalFileReader();

        Assert.Equal(2048, (await reader.ReadAsync(path, 4096, TestContext.Current.CancellationToken)).Length);
        var tooLarge = await Assert.ThrowsAsync<Core.Errors.RetuneException>(() => reader.ReadAsync(path, 1024, TestContext.Current.CancellationToken));
        Assert.Equal(Core.Errors.ProblemKind.ResponseTooLarge, tooLarge.Kind);
        await Assert.ThrowsAsync<Core.Errors.RetuneException>(() => reader.ReadAsync(_temp.File("missing.m3u"), 1024, TestContext.Current.CancellationToken));
    }

    private static Catalog SampleCatalog(int size)
    {
        var channels = Enumerable.Range(0, size / 4).Select(i => new Channel($"{i}", i + 1, $"Channel {i}", new Uri($"http://img.example/c/{i}.png"), $"{i % 40}", null, null, false)).ToList();
        var movies = Enumerable.Range(0, size / 2).Select(i => new Movie($"{i}", $"Movie {i}", new Uri($"http://img.example/m/{i}.jpg"), $"{i % 80}", i % 10, T0.AddMinutes(-i), "mkv", null)).ToList();
        var series = Enumerable.Range(0, size / 4).Select(i => new Series($"{i}", $"Series {i}", null, null, $"{i % 20}", "Plot", "Drama", "2020-01-01", 7.5, T0)).ToList();
        return new Catalog(T0, [new("1", "News")], [new("2", "Films")], [new("3", "Shows")], channels, movies, series);
    }
}
