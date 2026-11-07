using Microsoft.Extensions.Time.Testing;
using Retune.Core.Errors;
using Retune.Core.Models;
using Retune.Core.Net;
using Retune.Core.Tests.Support;
using Retune.Core.Xtream;

namespace Retune.Core.Tests.Xtream;

public sealed class XtreamClientTests
{
    private static readonly XtreamCredentials Creds = new("http://h.example:8080", "u", "p");

    /// <summary>Runs <paramref name="call"/> while advancing the fake clock so retry waits complete instantly.</summary>
    private static async Task<T> RunWithClock<T>(FakeTimeProvider time, Func<Task<T>> call)
    {
        var task = call();
        for (var i = 0; i < 20 && !task.IsCompleted; i++)
        {
            await Task.Yield();
            time.Advance(TimeSpan.FromMilliseconds(700));
        }

        return await task;
    }

    [Fact]
    public async Task Empty_body_and_throttling_are_retried_then_succeed()
    {
        var fetcher = new FakeFetcher().Then(200).Then(222).Then(200, """{"info": {"plot": "Worth the wait"}}""");
        var time = new FakeTimeProvider();
        var client = new XtreamClient(Creds, fetcher, time);

        var info = await RunWithClock(time, () => client.MovieInfoAsync("7", TestContext.Current.CancellationToken));

        Assert.Equal(3, fetcher.Requests.Count);
        Assert.Equal("Worth the wait", info.Plot);
        Assert.All(fetcher.Requests, r => Assert.EndsWith("action=get_vod_info&vod_id=7", r, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retries_wait_700_then_1400_ms()
    {
        var fetcher = new FakeFetcher().Then(503).Then(503).Then(200, "{}");
        var time = new FakeTimeProvider();
        var client = new XtreamClient(Creds, fetcher, time);

        var call = client.ShortEpgAsync("1", TestContext.Current.CancellationToken);
        await Task.Yield();
        Assert.Single(fetcher.Requests);

        time.Advance(TimeSpan.FromMilliseconds(699));
        await Task.Yield();
        Assert.Single(fetcher.Requests);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntil(() => fetcher.Requests.Count == 2);
        await Task.Delay(50, TestContext.Current.CancellationToken); // let the retry loop register its next wait

        time.Advance(TimeSpan.FromMilliseconds(1399));
        await Task.Yield();
        Assert.Equal(2, fetcher.Requests.Count);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await call;
        Assert.Equal(3, fetcher.Requests.Count);
    }

    [Fact]
    public async Task A_persistent_empty_body_is_an_error_not_an_empty_section()
    {
        var fetcher = new FakeFetcher(_ => new HttpBody(200, ReadOnlyMemory<byte>.Empty));
        var time = new FakeTimeProvider();
        var client = new XtreamClient(Creds, fetcher, time);

        var ex = await Assert.ThrowsAsync<RetuneException>(() => RunWithClock(time, () => client.FetchCatalogAsync(TestContext.Current.CancellationToken)));

        Assert.Equal(ProblemKind.EmptyResponse, ex.Kind);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Client_errors_fail_immediately(int status)
    {
        var fetcher = new FakeFetcher().Then(status);
        var client = new XtreamClient(Creds, fetcher, new FakeTimeProvider());

        var ex = await Assert.ThrowsAsync<RetuneException>(() => client.MovieInfoAsync("1", TestContext.Current.CancellationToken));

        Assert.Equal(ProblemKind.Http, ex.Kind);
        Assert.Equal(status, ex.HttpStatus);
        Assert.Single(fetcher.Requests);
    }

    [Fact]
    public async Task Timeouts_are_retried_but_oversized_bodies_are_not()
    {
        var time = new FakeTimeProvider();
        var retried = new FakeFetcher().ThenFail(FetchFailure.Timeout).ThenFail(FetchFailure.ConnectionLost).Then(200, "{}");
        await RunWithClock(time, () => new XtreamClient(Creds, retried, time).ShortEpgAsync("1", TestContext.Current.CancellationToken));
        Assert.Equal(3, retried.Requests.Count);

        var huge = new FakeFetcher().ThenFail(FetchFailure.TooLarge);
        var ex = await Assert.ThrowsAsync<RetuneException>(() => new XtreamClient(Creds, huge, time).ShortEpgAsync("1", TestContext.Current.CancellationToken));
        Assert.Equal(ProblemKind.ResponseTooLarge, ex.Kind);
        Assert.Single(huge.Requests);
    }

    [Fact]
    public async Task Authentication_accepts_active_accounts_only()
    {
        var active = new FakeFetcher().Then(200, """{"user_info": {"auth": 1, "status": "Active"}}""");
        Assert.True((await new XtreamClient(Creds, active, TimeProvider.System).AuthenticateAsync(TestContext.Current.CancellationToken)).IsActive);

        var expired = new FakeFetcher().Then(200, """{"user_info": {"auth": 1, "status": "Expired"}}""");
        var inactive = await Assert.ThrowsAsync<RetuneException>(() => new XtreamClient(Creds, expired, TimeProvider.System).AuthenticateAsync(TestContext.Current.CancellationToken));
        Assert.Equal("This account is expired.", inactive.Message);

        var rejected = new FakeFetcher().Then(200, """{"user_info": {"auth": 0}}""");
        var bad = await Assert.ThrowsAsync<RetuneException>(() => new XtreamClient(Creds, rejected, TimeProvider.System).AuthenticateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ProblemKind.InvalidCredentials, bad.Kind);
    }

    [Fact]
    public async Task Fetches_the_whole_catalog()
    {
        var fetcher = new FakeFetcher(url => url switch
        {
            _ when url.Contains("get_live_categories", StringComparison.Ordinal) => FakeFetcher.Ok("""[{"category_id": 1, "category_name": "News"}]"""),
            _ when url.Contains("get_live_streams", StringComparison.Ordinal) => FakeFetcher.Ok("""[{"stream_id": 10, "category_id": 1}]"""),
            _ when url.Contains("get_vod_streams", StringComparison.Ordinal) => FakeFetcher.Ok("""[{"stream_id": 20}, {"stream_id": 21}]"""),
            _ => FakeFetcher.Ok("[]"),
        });
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

        var catalog = await new XtreamClient(Creds, fetcher, time).FetchCatalogAsync(TestContext.Current.CancellationToken);

        Assert.Single(catalog.LiveCategories);
        Assert.Single(catalog.Channels);
        Assert.Equal(2, catalog.Movies.Count);
        Assert.Empty(catalog.Series);
        Assert.Equal(time.GetUtcNow(), catalog.FetchedAt);
        Assert.Equal(6, fetcher.Requests.Count);
        Assert.DoesNotContain(fetcher.Requests, r => r.Contains("category_id", StringComparison.Ordinal));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
