using Retune.Core.Errors;
using Retune.Core.Models;
using Retune.Core.Net;

namespace Retune.Core.Xtream;

/// <summary>Talks to one Xtream Codes account. Spec: docs/spec/core.md §2.3–2.5.</summary>
public sealed class XtreamClient(XtreamCredentials credentials, IHttpFetcher fetcher, TimeProvider time)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryStep = TimeSpan.FromMilliseconds(700);

    public XtreamUrls Urls { get; } = new(credentials);

    /// <summary>Verifies the login and returns the account. Throws when rejected or inactive.</summary>
    public async Task<AccountInfo> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var body = await FetchAsync(action: null, extra: null, cancellationToken).ConfigureAwait(false);
        var account = XtreamDecoder.Auth(body) ?? throw RetuneException.InvalidCredentials();
        return account.IsActive ? account : throw RetuneException.AccountInactive(account.Status);
    }

    /// <summary>Downloads the full catalog: categories in parallel (fail fast), then the three stream lists.</summary>
    public async Task<Catalog> FetchCatalogAsync(CancellationToken cancellationToken = default)
    {
        var liveCategories = ListAsync("get_live_categories", XtreamDecoder.Categories, cancellationToken);
        var vodCategories = ListAsync("get_vod_categories", XtreamDecoder.Categories, cancellationToken);
        var seriesCategories = ListAsync("get_series_categories", XtreamDecoder.Categories, cancellationToken);
        await Task.WhenAll(liveCategories, vodCategories, seriesCategories).ConfigureAwait(false);

        var channels = ListAsync("get_live_streams", XtreamDecoder.LiveStreams, cancellationToken);
        var movies = ListAsync("get_vod_streams", XtreamDecoder.VodStreams, cancellationToken);
        var series = ListAsync("get_series", XtreamDecoder.SeriesList, cancellationToken);
        await Task.WhenAll(channels, movies, series).ConfigureAwait(false);

        return new Catalog(
            time.GetUtcNow(),
            await liveCategories.ConfigureAwait(false),
            await vodCategories.ConfigureAwait(false),
            await seriesCategories.ConfigureAwait(false),
            await channels.ConfigureAwait(false),
            await movies.ConfigureAwait(false),
            await series.ConfigureAwait(false));
    }

    public async Task<MediaInfo> MovieInfoAsync(string movieId, CancellationToken cancellationToken = default)
    {
        var body = await FetchAsync("get_vod_info", new Dictionary<string, string> { ["vod_id"] = movieId }, cancellationToken).ConfigureAwait(false);
        return XtreamDecoder.VodInfo(body);
    }

    public async Task<SeriesDetail> SeriesInfoAsync(string seriesId, CancellationToken cancellationToken = default)
    {
        var body = await FetchAsync("get_series_info", new Dictionary<string, string> { ["series_id"] = seriesId }, cancellationToken).ConfigureAwait(false);
        return XtreamDecoder.SeriesInfo(body);
    }

    public async Task<IReadOnlyList<EpgEntry>> ShortEpgAsync(string streamId, CancellationToken cancellationToken = default)
    {
        var extra = new Dictionary<string, string> { ["stream_id"] = streamId, ["limit"] = "4" };
        var body = await FetchAsync("get_short_epg", extra, cancellationToken).ConfigureAwait(false);
        return XtreamDecoder.ShortEpg(body);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(string action, Func<ReadOnlyMemory<byte>, IReadOnlyList<T>> decode, CancellationToken cancellationToken)
    {
        var body = await FetchAsync(action, null, cancellationToken).ConfigureAwait(false);
        return decode(body);
    }

    /// <summary>
    /// Up to 3 attempts, waiting 700 ms then 1,400 ms. Retries empty bodies (panels throttle with them),
    /// 222, 429, 5xx, timeouts and dropped connections; anything else fails at once.
    /// </summary>
    private async Task<ReadOnlyMemory<byte>> FetchAsync(string? action, IReadOnlyDictionary<string, string>? extra, CancellationToken cancellationToken)
    {
        var url = Urls.Api(action, extra);
        RetuneException? last = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryStep * attempt, time, cancellationToken).ConfigureAwait(false);
            }

            HttpBody response;
            try
            {
                response = await fetcher.GetAsync(url, FetchLimits.MaxApiBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (FetchException ex) when (ex.Failure is FetchFailure.Timeout or FetchFailure.ConnectionLost)
            {
                last = ex.Failure == FetchFailure.Timeout ? RetuneException.Timeout(ex) : RetuneException.ConnectionLost(ex);
                continue;
            }
            catch (FetchException ex)
            {
                throw ex.Failure == FetchFailure.TooLarge ? RetuneException.ResponseTooLarge() : RetuneException.Network(ex.Message, ex);
            }

            var status = response.StatusCode ?? 200;
            if (status == 200)
            {
                if (!response.Content.IsEmpty || action is null)
                {
                    return response.Content;
                }

                last = RetuneException.EmptyResponse();
            }
            else if (status is 222 or 429 or >= 500)
            {
                last = RetuneException.Http(status);
            }
            else
            {
                throw RetuneException.Http(status);
            }
        }

        throw last!;
    }
}
