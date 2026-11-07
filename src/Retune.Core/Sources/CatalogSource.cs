using Retune.Core.Errors;
using Retune.Core.M3u;
using Retune.Core.Models;
using Retune.Core.Net;
using Retune.Core.Persistence;
using Retune.Core.Xtream;

namespace Retune.Core.Sources;

/// <summary>Downloads a playlist's catalog from wherever it lives: an Xtream account, an M3U URL or an M3U file.</summary>
public sealed class CatalogSource(IHttpFetcher fetcher, ILocalFileReader files, ISecretStore secrets, TimeProvider time)
{
    /// <summary>A client for an Xtream playlist with a stored password, else null.</summary>
    public XtreamClient? XtreamClientFor(Playlist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        return playlist.Source is XtreamSource xtream && secrets.Find(playlist.Id) is { } password
            ? new XtreamClient(new XtreamCredentials(xtream.Server, xtream.Username, password), fetcher, time)
            : null;
    }

    public async Task<Catalog> FetchAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        switch (playlist.Source)
        {
            case XtreamSource:
                var client = XtreamClientFor(playlist) ?? throw RetuneException.MissingPassword();
                return await client.FetchCatalogAsync(cancellationToken).ConfigureAwait(false);
            case M3uSource { IsLocalFile: true } file:
                var bytes = await files.ReadAsync(file.Location, FetchLimits.MaxApiBytes, cancellationToken).ConfigureAwait(false);
                return await ParseAsync(bytes, cancellationToken).ConfigureAwait(false);
            case M3uSource remote:
                return await ParseAsync(await DownloadAsync(remote.Location, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            default:
                throw new NotSupportedException($"Unknown playlist source {playlist.Source.GetType().Name}.");
        }
    }

    /// <summary>One attempt, no retries (spec §3.1); any status but 200 is an error.</summary>
    private async Task<ReadOnlyMemory<byte>> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        HttpBody body;
        try
        {
            body = await fetcher.GetAsync(url, FetchLimits.MaxApiBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (FetchException ex)
        {
            throw RetuneException.FromFetch(ex);
        }

        return (body.StatusCode ?? 200) == 200 ? body.Content : throw RetuneException.Http(body.StatusCode!.Value);
    }

    private Task<Catalog> ParseAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        Task.Run(() => M3uParser.BuildCatalog(M3uParser.Parse(M3uParser.DecodeText(bytes.Span)), time.GetUtcNow()), cancellationToken);
}
