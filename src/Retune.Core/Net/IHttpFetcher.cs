namespace Retune.Core.Net;

/// <summary>
/// The only way Core reaches the network. Implementations must use no HTTP cache and no cookies
/// (Xtream puts the password in the URL), stream the body, and stop at <c>maxBytes</c>.
/// </summary>
public interface IHttpFetcher
{
    /// <exception cref="FetchException">Transport failures, including an oversized body.</exception>
    Task<HttpBody> GetAsync(string url, long maxBytes, CancellationToken cancellationToken);
}

/// <param name="StatusCode">Null when the transport had none (treated as 200).</param>
public sealed record HttpBody(int? StatusCode, ReadOnlyMemory<byte> Content);

public enum FetchFailure
{
    Timeout,
    ConnectionLost,
    TooLarge,
    Other,
}

/// <summary>A transport-level failure. Messages must never contain the URL.</summary>
public sealed class FetchException : Exception
{
    public FetchException()
    {
    }

    public FetchException(string message)
        : base(message)
    {
    }

    public FetchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FetchException(FetchFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public FetchFailure Failure { get; } = FetchFailure.Other;
}

public static class FetchLimits
{
    /// <summary>API responses and playlists (256 MiB).</summary>
    public const long MaxApiBytes = 256L << 20;

    /// <summary>Artwork (32 MiB).</summary>
    public const long MaxArtworkBytes = 16L << 20;
}
