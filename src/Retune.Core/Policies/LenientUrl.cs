namespace Retune.Core.Policies;

/// <summary>Forgiving http(s) URL parsing for provider artwork and the M3U URL field.</summary>
public static class LenientUrl
{
    /// <summary>RFC 3986 reserved and unreserved characters, plus '%' so existing escapes survive.</summary>
    private const string UrlLiterals = ":/?#[]@!$&'()*+,;=-._~%";

    /// <summary>
    /// Trims, unescapes JSON <c>\/</c>, requires an http(s) prefix, and parses. If parsing fails,
    /// characters a URL can't hold (spaces and so on) are percent-encoded and parsing is retried.
    /// </summary>
    public static Uri? Parse(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        text = text.Replace("\\/", "/", StringComparison.Ordinal);
        if (!UrlPolicy.HasHttpScheme(text))
        {
            return null;
        }

        var parsed = TryParse(text)
            ?? TryParse(PercentEncoding.Encode(text, static c => char.IsAsciiLetterOrDigit(c) || UrlLiterals.Contains(c, StringComparison.Ordinal)));
        return UrlPolicy.IsAllowedArtwork(parsed) ? parsed : null;
    }

    private static Uri? TryParse(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
}
