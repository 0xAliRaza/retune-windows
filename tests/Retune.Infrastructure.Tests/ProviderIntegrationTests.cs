using System.Diagnostics;
using Retune.Core.Errors;
using Retune.Core.Models;
using Retune.Core.Persistence;
using Retune.Core.Search;
using Retune.Core.Sources;
using Retune.FakeProvider;
using Retune.Infrastructure.Net;
using Retune.Infrastructure.Storage;

namespace Retune.Infrastructure.Tests;

/// <summary>Real HTTP, real decoding, against the local fake provider.</summary>
public sealed class ProviderIntegrationTests : IAsyncDisposable
{
    private readonly HttpFetcher _fetcher = new(4, "Retune-Tests/1.0");
    private readonly MemorySecrets _secrets = new();

    public async ValueTask DisposeAsync()
    {
        _fetcher.Dispose();
        await ValueTask.CompletedTask;
    }

    /// <summary>Real clock: retries must really wait here (a fake clock nobody advances would hang them).</summary>
    private CatalogSource Source() => new(_fetcher, new LocalFileReader(), _secrets, TimeProvider.System);

    [Fact]
    public async Task Xtream_login_and_full_catalog_download()
    {
        await using var provider = await FakeProviderServer.StartAsync(new FakeProviderOptions());
        var playlist = new Playlist(Guid.NewGuid(), "Demo", new XtreamSource(provider.BaseUrl, "demo"), DateTimeOffset.UtcNow);
        _secrets.Store(playlist.Id, "demo");
        var client = Source().XtreamClientFor(playlist)!;

        var account = await client.AuthenticateAsync(TestContext.Current.CancellationToken);
        var catalog = await Source().FetchAsync(playlist, TestContext.Current.CancellationToken);
        var detail = await client.SeriesInfoAsync(catalog.Series[0].Id, TestContext.Current.CancellationToken);
        var epg = await client.ShortEpgAsync(catalog.Channels[0].Id, TestContext.Current.CancellationToken);

        Assert.True(account.IsActive);
        Assert.Equal(["m3u8", "ts"], account.Formats);
        Assert.Equal(120, catalog.Channels.Count);
        Assert.Equal(400, catalog.Movies.Count);
        Assert.Equal(80, catalog.Series.Count);
        Assert.Equal(8, catalog.LiveCategories.Count);
        Assert.NotEmpty(detail.Seasons);
        Assert.Equal(4, epg.Count);
        Assert.DoesNotContain(epg, e => e.Title.Length == 0);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        await using var provider = await FakeProviderServer.StartAsync(new FakeProviderOptions());
        var playlist = new Playlist(Guid.NewGuid(), "Demo", new XtreamSource(provider.BaseUrl, "demo"), DateTimeOffset.UtcNow);
        _secrets.Store(playlist.Id, "wrong");

        var ex = await Assert.ThrowsAsync<RetuneException>(() => Source().XtreamClientFor(playlist)!.AuthenticateAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ProblemKind.InvalidCredentials, ex.Kind);
    }

    [Fact]
    public async Task An_xtream_playlist_without_a_stored_password_fails_cleanly()
    {
        var playlist = new Playlist(Guid.NewGuid(), "Demo", new XtreamSource("http://127.0.0.1:1", "demo"), DateTimeOffset.UtcNow);

        var ex = await Assert.ThrowsAsync<RetuneException>(() => Source().FetchAsync(playlist, TestContext.Current.CancellationToken));

        Assert.Equal(ProblemKind.MissingPassword, ex.Kind);
    }

    [Fact]
    public async Task M3u_url_download_and_parse()
    {
        await using var provider = await FakeProviderServer.StartAsync(new FakeProviderOptions());
        var playlist = new Playlist(Guid.NewGuid(), "M3U", new M3uSource($"{provider.BaseUrl}/get.php?username=demo&password=demo&type=m3u_plus"), DateTimeOffset.UtcNow);

        var catalog = await Source().FetchAsync(playlist, TestContext.Current.CancellationToken);

        Assert.Equal(120, catalog.Channels.Count);
        Assert.All(catalog.Channels, c => Assert.DoesNotContain("demo/demo", c.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_m3u_url_error_status_is_reported()
    {
        await using var provider = await FakeProviderServer.StartAsync(new FakeProviderOptions());
        var playlist = new Playlist(Guid.NewGuid(), "M3U", new M3uSource($"{provider.BaseUrl}/get.php?username=demo&password=nope"), DateTimeOffset.UtcNow);

        var ex = await Assert.ThrowsAsync<RetuneException>(() => Source().FetchAsync(playlist, TestContext.Current.CancellationToken));

        Assert.Equal(401, ex.HttpStatus);
    }

    [Fact]
    public async Task An_88k_item_account_downloads_and_indexes_quickly()
    {
        await using var provider = await FakeProviderServer.StartAsync(new FakeProviderOptions(Channels: 16_000, Movies: 65_000, Series: 7_000));
        var playlist = new Playlist(Guid.NewGuid(), "Big", new XtreamSource(provider.BaseUrl, "demo"), DateTimeOffset.UtcNow);
        _secrets.Store(playlist.Id, "demo");

        var watch = Stopwatch.StartNew();
        var catalog = await Source().FetchAsync(playlist, TestContext.Current.CancellationToken);
        var download = watch.Elapsed;
        var index = await CatalogIndex.BuildAsync(catalog, TestContext.Current.CancellationToken);
        var total = watch.Elapsed;

        Assert.Equal(88_000, catalog.Channels.Count + catalog.Movies.Count + catalog.Series.Count);
        Assert.NotEmpty(index.Movies.Select(new BrowseQuery(Search: "midnight")));
        Assert.True(total < TimeSpan.FromSeconds(15), $"Download {download}, total {total}.");
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<Guid, string> _secrets = [];

        public string? Find(Guid playlistId) => _secrets.GetValueOrDefault(playlistId);

        public void Store(Guid playlistId, string secret) => _secrets[playlistId] = secret;

        public void Remove(Guid playlistId) => _secrets.Remove(playlistId);
    }
}
