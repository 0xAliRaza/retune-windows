using System.Globalization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Retune.FakeProvider;

/// <summary>
/// A local Xtream Codes + M3U provider with made-up content, for development, screenshots, and end-to-end
/// and performance tests. Listens on localhost only.
/// </summary>
public sealed class FakeProviderServer : IAsyncDisposable
{
    private static readonly string[] AllowedFormats = ["m3u8", "ts"];
    private static readonly string[] StreamKinds = ["live", "movie", "series"];

    private readonly WebApplication _app;

    private FakeProviderServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    /// <summary>Requests served so far (tests use it to check retries and caching).</summary>
    public int RequestCount { get; private set; }

    /// <param name="port">0 picks a free port.</param>
    public static async Task<FakeProviderServer> StartAsync(FakeProviderOptions options, int port = 0)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        // The slim builder turns off reflection-based JSON; this dev tool serialises anonymous objects freely.
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}");
        var app = builder.Build();
        var catalog = new DemoCatalog(options);
        FakeProviderServer? server = null;

        app.Use(async (context, next) =>
        {
            server!.RequestCount++;
            await next(context).ConfigureAwait(false);
        });
        app.MapGet("/player_api.php", (HttpContext context) => PlayerApi(context, catalog));
        app.MapGet("/get.php", (HttpContext context) =>
            Authorized(context.Request.Query, options)
                ? Results.Text(catalog.M3u($"{context.Request.Scheme}://{context.Request.Host}"), "audio/x-mpegurl")
                : Results.StatusCode(401));
        foreach (var kind in StreamKinds)
        {
            app.MapGet($"/{kind}/{{user}}/{{password}}/{{file}}", (string user, string password) =>
                user == options.Username && password == options.Password ? Stream(options, kind) : Results.StatusCode(401));
        }
        app.MapGet("/art/{kind}/{name}", (string kind, string name) =>
        {
            var (w, h) = kind switch { "logo" => (160, 100), "backdrop" => (640, 360), "still" => (320, 180), _ => (200, 300) };
            return Results.Bytes(PngArt.Gradient($"{kind}/{name}", w, h), "image/png");
        });

        await app.StartAsync().ConfigureAwait(false);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server = new FakeProviderServer(app, address);
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static IResult PlayerApi(HttpContext context, DemoCatalog catalog)
    {
        var query = context.Request.Query;
        if (!Authorized(query, catalog.Options))
        {
            return Results.Json(new { user_info = new { auth = 0 } });
        }

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        int IdParam(string name) => int.TryParse(query[name], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return query["action"].ToString() switch
        {
            "" => Results.Json(new
            {
                user_info = new
                {
                    username = catalog.Options.Username,
                    auth = 1,
                    status = "Active",
                    exp_date = "1924992000",
                    is_trial = "0",
                    active_cons = "0",
                    max_connections = "2",
                    allowed_output_formats = AllowedFormats,
                },
                server_info = new { timezone = "UTC" },
            }),
            "get_live_categories" => Results.Json(DemoCatalog.Categories("live")),
            "get_vod_categories" => Results.Json(DemoCatalog.Categories("vod")),
            "get_series_categories" => Results.Json(DemoCatalog.Categories("series")),
            "get_live_streams" => Results.Json(catalog.LiveStreams(baseUrl)),
            "get_vod_streams" => Results.Json(catalog.VodStreams(baseUrl)),
            "get_series" => Results.Json(catalog.SeriesList(baseUrl)),
            "get_vod_info" => Results.Json(DemoCatalog.VodInfo(IdParam("vod_id"), baseUrl)),
            "get_series_info" => Results.Json(DemoCatalog.SeriesInfo(IdParam("series_id"), baseUrl)),
            "get_short_epg" => Results.Json(DemoCatalog.ShortEpg(IdParam("stream_id"), DateTimeOffset.UtcNow)),
            _ => Results.Json(Array.Empty<object>()),
        };
    }

    private static bool Authorized(IQueryCollection query, FakeProviderOptions options) =>
        query["username"] == options.Username && query["password"] == options.Password;

    /// <summary>Serves the synthetic test media (tools/make-test-media.ps1) with range support, or 404 without it.</summary>
    private static IResult Stream(FakeProviderOptions options, string kind)
    {
        var file = kind == "live" ? "live-720p.ts" : "vod-1080p.mp4";
        var path = options.MediaFolder is { } folder ? Path.Combine(folder, file) : null;
        return path is not null && File.Exists(path)
            ? Results.File(path, kind == "live" ? "video/mp2t" : "video/mp4", enableRangeProcessing: true)
            : Results.NotFound();
    }
}
