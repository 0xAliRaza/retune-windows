using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Retune.Core.Net;
using Retune.Infrastructure.Net;
using Retune.Infrastructure.Tests.Support;

namespace Retune.Infrastructure.Tests;

public sealed class ArtworkCacheTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly StubFetcher _fetcher = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    public void Dispose() => _temp.Dispose();

    private ArtworkCache Cache(long maxBytes = ArtworkCache.DefaultMaxBytes) => new(_temp.Path, _fetcher, maxBytes, _time);

    private static Uri U(string path) => new($"http://img.example/{path}");

    [Fact]
    public async Task Downloads_once_then_serves_from_disk()
    {
        _fetcher.Respond(U("a.png"), 200, [1, 2, 3]);
        var cache = Cache();

        Assert.Equal([1, 2, 3], await cache.GetAsync(U("a.png"), TestContext.Current.CancellationToken));
        Assert.Equal([1, 2, 3], await Cache().GetAsync(U("a.png"), TestContext.Current.CancellationToken));
        Assert.Equal(1, _fetcher.Calls(U("a.png")));
    }

    [Fact]
    public async Task Concurrent_requests_for_the_same_url_share_one_download()
    {
        _fetcher.Respond(U("a.png"), 200, [1], delay: TimeSpan.FromMilliseconds(100));
        var cache = Cache();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(U("a.png"), TestContext.Current.CancellationToken)));

        Assert.All(results, r => Assert.Equal([1], r));
        Assert.Equal(1, _fetcher.Calls(U("a.png")));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Missing_images_are_not_retried_this_session(int status)
    {
        _fetcher.Respond(U("gone.png"), status, []);
        var cache = Cache();

        Assert.Null(await cache.GetAsync(U("gone.png"), TestContext.Current.CancellationToken));
        Assert.Null(await cache.GetAsync(U("gone.png"), TestContext.Current.CancellationToken));
        Assert.Equal(1, _fetcher.Calls(U("gone.png")));
    }

    [Fact]
    public async Task Server_errors_and_timeouts_are_retried()
    {
        _fetcher.Respond(U("flaky.png"), 503, []);
        var cache = Cache();
        Assert.Null(await cache.GetAsync(U("flaky.png"), TestContext.Current.CancellationToken));

        _fetcher.Fail(U("slow.png"), FetchFailure.Timeout);
        Assert.Null(await cache.GetAsync(U("slow.png"), TestContext.Current.CancellationToken));

        _fetcher.Respond(U("flaky.png"), 200, [9]);
        Assert.Equal([9], await cache.GetAsync(U("flaky.png"), TestContext.Current.CancellationToken));
        Assert.Equal(2, _fetcher.Calls(U("flaky.png")));
    }

    [Fact]
    public async Task Undecodable_images_are_dropped_and_not_retried()
    {
        _fetcher.Respond(U("junk.png"), 200, [0xBA, 0xD]);
        var cache = Cache();
        await cache.GetAsync(U("junk.png"), TestContext.Current.CancellationToken);

        cache.MarkUndecodable(U("junk.png"));

        Assert.Null(await cache.GetAsync(U("junk.png"), TestContext.Current.CancellationToken));
        Assert.Equal(1, _fetcher.Calls(U("junk.png")));
    }

    [Fact]
    public async Task Non_http_artwork_is_never_fetched()
    {
        Assert.Null(await Cache().GetAsync(new Uri("file:///C:/Windows/win.ini"), TestContext.Current.CancellationToken));
        Assert.Empty(_fetcher.AllCalls);
    }

    [Fact]
    public async Task Eviction_removes_the_least_recently_used_first()
    {
        var cache = Cache(maxBytes: 3000);
        foreach (var name in new[] { "old", "mid", "new" })
        {
            _fetcher.Respond(U(name), 200, new byte[1000]);
            await cache.GetAsync(U(name), TestContext.Current.CancellationToken);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        await cache.GetAsync(U("old"), TestContext.Current.CancellationToken); // touch: now most recent
        _time.Advance(TimeSpan.FromMinutes(1));
        _fetcher.Respond(U("newest"), 200, new byte[1000]);
        await cache.GetAsync(U("newest"), TestContext.Current.CancellationToken);
        cache.Trim();

        var fresh = Cache(maxBytes: 3000);
        await fresh.GetAsync(U("mid"), TestContext.Current.CancellationToken);
        await fresh.GetAsync(U("old"), TestContext.Current.CancellationToken);
        Assert.Equal(2, _fetcher.Calls(U("mid")));
        Assert.Equal(1, _fetcher.Calls(U("old")));
    }

    [Fact]
    public async Task Clear_empties_the_disk_and_negative_caches()
    {
        _fetcher.Respond(U("a.png"), 200, [1]);
        _fetcher.Respond(U("gone.png"), 404, []);
        var cache = Cache();
        await cache.GetAsync(U("a.png"), TestContext.Current.CancellationToken);
        await cache.GetAsync(U("gone.png"), TestContext.Current.CancellationToken);

        cache.Clear();
        _fetcher.Respond(U("gone.png"), 200, [2]);

        Assert.Equal([1], await cache.GetAsync(U("a.png"), TestContext.Current.CancellationToken));
        Assert.Equal([2], await cache.GetAsync(U("gone.png"), TestContext.Current.CancellationToken));
        Assert.Equal(2, _fetcher.Calls(U("a.png")));
    }

    private sealed class StubFetcher : IHttpFetcher
    {
        private readonly ConcurrentDictionary<string, Func<Task<HttpBody>>> _routes = new();
        private readonly ConcurrentQueue<string> _calls = new();

        public IReadOnlyCollection<string> AllCalls => _calls;

        public int Calls(Uri url) => _calls.Count(c => c == url.AbsoluteUri);

        public void Respond(Uri url, int status, byte[] body, TimeSpan? delay = null) =>
            _routes[url.AbsoluteUri] = async () =>
            {
                if (delay is { } d)
                {
                    await Task.Delay(d);
                }

                return new HttpBody(status, body);
            };

        public void Fail(Uri url, FetchFailure failure) =>
            _routes[url.AbsoluteUri] = () => throw new FetchException(failure, "simulated");

        public Task<HttpBody> GetAsync(string url, long maxBytes, CancellationToken cancellationToken)
        {
            _calls.Enqueue(url);
            return _routes.TryGetValue(url, out var route) ? route() : Task.FromResult(new HttpBody(404, ReadOnlyMemory<byte>.Empty));
        }
    }
}
