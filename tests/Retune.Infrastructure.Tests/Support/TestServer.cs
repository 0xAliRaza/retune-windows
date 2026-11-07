using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Retune.Infrastructure.Tests.Support;

/// <summary>An in-process Kestrel server on a random localhost port, for real HTTP tests.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public string Url(string path) => BaseUrl + path;

    public static async Task<TestServer> StartAsync(Action<WebApplication> routes)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        routes(app);
        await app.StartAsync().ConfigureAwait(false);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestServer(app, address);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Writes <paramref name="count"/> bytes without a Content-Length (chunked).</summary>
    public static async Task WriteChunked(HttpContext context, int count)
    {
        context.Response.ContentType = "application/octet-stream";
        await context.Response.StartAsync().ConfigureAwait(false);
        await context.Response.Body.WriteAsync(new byte[count]).ConfigureAwait(false);
    }
}
