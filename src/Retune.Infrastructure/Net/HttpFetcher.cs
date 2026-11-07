using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using Retune.Core.Net;

namespace Retune.Infrastructure.Net;

/// <summary>
/// The app's only HTTP client. No cookies and no HTTP cache (Xtream puts the password in the URL), an idle
/// timeout that restarts on every read, a total timeout, and a hard cap on the decompressed body size.
/// Error messages are written here and never contain the URL. Spec: docs/spec/core.md §2.5.
/// </summary>
public sealed class HttpFetcher : IHttpFetcher, IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(600);

    private const int ReadChunk = 81_920;
    private const int MaxRedirects = 5;

    private readonly HttpClient _client;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _totalTimeout;

    /// <param name="maxConnectionsPerServer">4 for provider APIs, 8 for artwork.</param>
    public HttpFetcher(int maxConnectionsPerServer, string userAgent, TimeSpan? idleTimeout = null, TimeSpan? totalTimeout = null)
    {
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _totalTimeout = totalTimeout ?? DefaultTotalTimeout;
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRedirects,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = maxConnectionsPerServer,
            ConnectTimeout = _idleTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse(userAgent));
    }

    public async Task<HttpBody> GetAsync(string url, long maxBytes, CancellationToken cancellationToken)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(_totalTimeout);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
        idle.CancelAfter(_idleTimeout);

        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw TooLarge();
            }

            var stream = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var body = await ReadCapped(stream, maxBytes, idle).ConfigureAwait(false);
                return new HttpBody((int)response.StatusCode, body);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FetchException(FetchFailure.Timeout, "The server took too long to respond.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw Translate(ex);
        }
        catch (IOException ex)
        {
            throw new FetchException(FetchFailure.ConnectionLost, "The connection to the server was lost.", ex);
        }
    }

    public void Dispose() => _client.Dispose();

    /// <summary>Reads to the end, restarting the idle timer on every chunk and stopping one byte past the cap.</summary>
    private async Task<ReadOnlyMemory<byte>> ReadCapped(Stream stream, long maxBytes, CancellationTokenSource idle)
    {
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(ReadChunk);
        try
        {
            while (true)
            {
                idle.CancelAfter(_idleTimeout);
                var read = await stream.ReadAsync(chunk.AsMemory(0, ReadChunk), idle.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.ToArray();
                }

                if (buffer.Length + read > maxBytes)
                {
                    throw TooLarge();
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private static FetchException TooLarge() =>
        new(FetchFailure.TooLarge, "The server sent far more data than expected.");

    private static FetchException Translate(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.ResponseEnded => new FetchException(FetchFailure.ConnectionLost, "The connection to the server was lost.", ex),
        HttpRequestError.NameResolutionError => new FetchException(FetchFailure.Other, "The server address couldn't be found. Check it and your internet connection.", ex),
        HttpRequestError.ConnectionError => new FetchException(FetchFailure.Other, "Couldn't connect to the server.", ex),
        HttpRequestError.SecureConnectionError => new FetchException(FetchFailure.Other, "A secure connection to the server couldn't be established.", ex),
        HttpRequestError.ProxyTunnelError => new FetchException(FetchFailure.Other, "The proxy server refused the connection.", ex),
        _ when ex.InnerException is IOException => new FetchException(FetchFailure.ConnectionLost, "The connection to the server was lost.", ex),
        _ => new FetchException(FetchFailure.Other, "The server couldn't be reached.", ex),
    };
}
