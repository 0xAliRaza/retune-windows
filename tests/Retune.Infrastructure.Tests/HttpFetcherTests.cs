using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Retune.Core.Net;
using Retune.Infrastructure.Net;
using Retune.Infrastructure.Tests.Support;

namespace Retune.Infrastructure.Tests;

public sealed class HttpFetcherTests : IAsyncLifetime
{
    private TestServer _server = null!;
    private readonly HttpFetcher _fetcher = new(4, "Retune-Tests/1.0", idleTimeout: TimeSpan.FromMilliseconds(500), totalTimeout: TimeSpan.FromSeconds(5));

    public async ValueTask InitializeAsync()
    {
        _server = await TestServer.StartAsync(app =>
        {
            app.MapGet("/ok", () => "hello");
            app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(code));
            app.MapGet("/empty", () => Results.Ok());
            app.MapGet("/big-chunked", context => TestServer.WriteChunked(context, 2048));
            app.MapGet("/big-declared", () => Results.Bytes(new byte[2048]));
            app.MapGet("/gzip-bomb", async context =>
            {
                using var compressed = new MemoryStream();
                await using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    await gzip.WriteAsync(new byte[1_000_000]);
                }

                context.Response.Headers.ContentEncoding = "gzip";
                await context.Response.Body.WriteAsync(compressed.ToArray());
            });
            app.MapGet("/stall", async context =>
            {
                await context.Response.StartAsync();
                await context.Response.Body.WriteAsync("partial"u8.ToArray());
                await context.Response.Body.FlushAsync();
                await Task.Delay(TimeSpan.FromSeconds(3), context.RequestAborted).ContinueWith(_ => { }, TaskScheduler.Default);
            });
            app.MapGet("/drop", async context =>
            {
                context.Response.ContentLength = 10_000;
                await context.Response.Body.WriteAsync(new byte[100]);
                await context.Response.Body.FlushAsync();
                context.Abort();
            });
            app.MapGet("/redirect", () => Results.Redirect("/ok"));
            app.MapGet("/set-cookie", context =>
            {
                context.Response.Headers.SetCookie = "session=abc; Path=/";
                return context.Response.WriteAsync("set");
            });
            app.MapGet("/echo", context => context.Response.WriteAsync($"cookie={context.Request.Headers.Cookie};ua={context.Request.Headers.UserAgent}"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        _fetcher.Dispose();
        await _server.DisposeAsync();
    }

    private Task<HttpBody> Get(string path, long maxBytes = 1024) =>
        _fetcher.GetAsync(_server.Url(path), maxBytes, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Returns_normal_bodies_unchanged()
    {
        var body = await Get("/ok");

        Assert.Equal(200, body.StatusCode);
        Assert.Equal("hello", Encoding.UTF8.GetString(body.Content.Span));
    }

    [Fact]
    public async Task Returns_status_codes_without_throwing()
    {
        Assert.Equal(503, (await Get("/status/503")).StatusCode);
        Assert.Equal(404, (await Get("/status/404")).StatusCode);
        Assert.True((await Get("/empty")).Content.IsEmpty);
    }

    [Theory]
    [InlineData("/big-chunked")]
    [InlineData("/big-declared")]
    [InlineData("/gzip-bomb")]
    public async Task Oversized_bodies_are_cut_off(string path)
    {
        var ex = await Assert.ThrowsAsync<FetchException>(() => Get(path, maxBytes: 1024));

        Assert.Equal(FetchFailure.TooLarge, ex.Failure);
    }

    [Fact]
    public async Task A_stalled_body_times_out()
    {
        var ex = await Assert.ThrowsAsync<FetchException>(() => Get("/stall"));

        Assert.Equal(FetchFailure.Timeout, ex.Failure);
    }

    [Fact]
    public async Task A_dropped_connection_is_reported_as_lost()
    {
        var ex = await Assert.ThrowsAsync<FetchException>(() => Get("/drop", maxBytes: 1_000_000));

        Assert.Equal(FetchFailure.ConnectionLost, ex.Failure);
    }

    [Fact]
    public async Task A_refused_connection_fails_without_leaking_the_url()
    {
        // Windows retries refused loopback connections for ~2 s, so this needs a realistic timeout.
        using var patient = new HttpFetcher(4, "Retune-Tests/1.0", idleTimeout: TimeSpan.FromSeconds(10));
        var url = "http://127.0.0.1:9/player_api.php?username=u&password=topsecret";

        var ex = await Assert.ThrowsAsync<FetchException>(() => patient.GetAsync(url, 1024, TestContext.Current.CancellationToken));

        Assert.Equal(FetchFailure.Other, ex.Failure);
        Assert.DoesNotContain("topsecret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Follows_redirects()
    {
        Assert.Equal("hello", Encoding.UTF8.GetString((await Get("/redirect")).Content.Span));
    }

    [Fact]
    public async Task Never_stores_or_sends_cookies_and_identifies_itself()
    {
        await Get("/set-cookie");

        var echo = Encoding.UTF8.GetString((await Get("/echo")).Content.Span);

        Assert.Equal("cookie=;ua=Retune-Tests/1.0", echo);
    }

    [Fact]
    public async Task The_app_user_agent_with_a_platform_comment_is_accepted()
    {
        using var app = new HttpFetcher(4, "Retune/0.1 (Windows)");

        var echo = Encoding.UTF8.GetString((await app.GetAsync(_server.Url("/echo"), 1024, TestContext.Current.CancellationToken)).Content.Span);

        Assert.EndsWith("ua=Retune/0.1 (Windows)", echo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _fetcher.GetAsync(_server.Url("/ok"), 1024, cts.Token));
    }
}
