using Retune.Core.Models;
using Retune.Core.Policies;

namespace Retune.Core.Xtream;

/// <summary>Turns what users type or paste into a clean Xtream server base URL.</summary>
public static class XtreamServer
{
    private static readonly string[] EndpointSuffixes = ["/player_api.php", "/get.php", "/xmltv.php"];

    /// <summary>
    /// Returns e.g. <c>http://iptv.example:8080</c>, or null if the input can't be a server.
    /// A bare host means plain HTTP. Query, fragment, known endpoint files and trailing slashes
    /// are removed; an explicit port (even :80) and any other path prefix are kept.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        if (!UrlPolicy.HasHttpScheme(text))
        {
            // Another explicit scheme (ftp://, rtmp://…) is not a server we can talk to.
            if (text.Contains("://", StringComparison.Ordinal))
            {
                return null;
            }

            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        // Work on the original text, not uri.AbsoluteUri: System.Uri would drop an explicit default port.
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal) + 3;
        var cut = text.IndexOfAny(['?', '#'], schemeEnd);
        var withoutQuery = cut >= 0 ? text[..cut] : text;
        var pathStart = withoutQuery.IndexOf('/', schemeEnd);
        var origin = pathStart >= 0 ? withoutQuery[..pathStart] : withoutQuery;
        var path = pathStart >= 0 ? withoutQuery[pathStart..] : string.Empty;

        foreach (var suffix in EndpointSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
            }
        }

        return uri.Scheme + origin[(origin.IndexOf("://", StringComparison.Ordinal))..] + path.TrimEnd('/');
    }

    /// <summary>True when the normalised server uses plain HTTP, so the login travels unencrypted.</summary>
    public static bool UsesPlainHttp(string? raw) =>
        Normalize(raw) is { } server && server.StartsWith("http://", StringComparison.Ordinal);

    /// <summary>
    /// Extracts credentials from a provider link such as
    /// <c>http://h:8080/get.php?username=u&amp;password=p&amp;type=m3u_plus</c>.
    /// Both the first <c>username</c> and first <c>password</c> must be present and non-empty.
    /// </summary>
    public static XtreamCredentials? ParseLink(string? link)
    {
        if (Normalize(link) is not { } server)
        {
            return null;
        }

        var text = link!.Trim();
        if (!Uri.TryCreate(UrlPolicy.HasHttpScheme(text) ? text : "http://" + text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var username = FirstQueryValue(uri.Query, "username");
        var password = FirstQueryValue(uri.Query, "password");
        return string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
            ? null
            : new XtreamCredentials(server, username, password);
    }

    /// <summary>Percent-decodes the first matching value. A literal '+' stays '+' (passwords may contain it).</summary>
    private static string? FirstQueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString(eq >= 0 ? pair[..eq] : pair);
            if (key == name)
            {
                return eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..]) : string.Empty;
            }
        }

        return null;
    }
}
