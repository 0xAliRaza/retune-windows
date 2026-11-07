using System.Collections.Frozen;

namespace Retune.Core.Policies;

/// <summary>
/// Which URLs Retune will hand to the player or fetch artwork from. Applied when parsing playlists
/// and again right before every load, because playlists are hostile input.
/// </summary>
public static class UrlPolicy
{
    private static readonly FrozenSet<string> StreamSchemes =
        new[] { "http", "https", "rtmp", "rtmps", "rtsp", "rtsps", "rtp", "udp" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True for network streams only (http(s), rtmp(s), rtsp(s), rtp, udp) with a host.
    /// Rejects file:, mpv protocols (av:, edl:, memory:, fd:, lavf:), javascript:, bare paths and look-alikes.
    /// </summary>
    public static bool IsAllowedStream(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && StreamSchemes.Contains(uri.Scheme)
        && !string.IsNullOrEmpty(uri.Host)
        && HasExplicitAuthority(url!, uri.Scheme);

    /// <summary>True when the text literally starts with <c>http://</c> or <c>https://</c> (any case).</summary>
    public static bool HasHttpScheme(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Artwork is stricter: http and https only.</summary>
    public static bool IsAllowedArtwork(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);

    /// <summary>"scheme://" must be literally present: rules out "http:host" style parses.</summary>
    private static bool HasExplicitAuthority(string url, string scheme) =>
        url.AsSpan().TrimStart().StartsWith($"{scheme}://", StringComparison.OrdinalIgnoreCase);
}
