using Retune.Core.Models;

namespace Retune.Core.Xtream;

/// <summary>
/// Watch history never stores a stream URL (Xtream URLs contain the password). It stores
/// <c>retune://</c> locators that are resolved against the current credentials at play time,
/// so history also survives password or server changes.
/// </summary>
public static class Locators
{
    private const string Prefix = "retune://";
    private const string OpaqueHost = "item";

    private static readonly string[] StreamKinds = [XtreamUrls.LivePath, XtreamUrls.MoviePath, XtreamUrls.SeriesPath];

    public static bool IsLocator(string url) => url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>http://h:8080/movie/u/secret/9.mkv</c> → <c>retune://movie/9.mkv</c>. Works on the encoded path,
    /// so a '/' inside credentials can't shift the segments. Null when the URL isn't an Xtream stream URL.
    /// </summary>
    public static string? FromXtreamStream(string streamUrl)
    {
        ArgumentNullException.ThrowIfNull(streamUrl);
        var schemeEnd = streamUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return null;
        }

        var afterScheme = streamUrl[(schemeEnd + 3)..];
        var cut = afterScheme.IndexOfAny(['?', '#']);
        var hostAndPath = cut >= 0 ? afterScheme[..cut] : afterScheme;
        var pathStart = hostAndPath.IndexOf('/', StringComparison.Ordinal);
        if (pathStart < 0)
        {
            return null;
        }

        var segments = hostAndPath[pathStart..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
        {
            return null;
        }

        var kind = segments[^4];
        return StreamKinds.Contains(kind, StringComparer.Ordinal) ? $"{Prefix}{kind}/{segments[^1]}" : null;
    }

    /// <summary><c>retune://item/&lt;id&gt;</c> for M3U items. The id is a URL hash, never the URL itself.</summary>
    public static string Opaque(string id) => $"{Prefix}{OpaqueHost}/{id}";

    /// <summary>
    /// The item as it may be stored: locators are kept; with an account the Xtream locator is used and,
    /// if none can be derived, nothing is stored (deliberately no raw-URL fallback); otherwise opaque.
    /// </summary>
    public static PlaybackItem? CredentialFree(PlaybackItem item, bool hasAccount)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (IsLocator(item.Url))
        {
            return item;
        }

        if (hasAccount)
        {
            return FromXtreamStream(item.Url) is { } locator ? item with { Url = locator } : null;
        }

        return item with { Url = Opaque(item.Id) };
    }

    /// <summary>Rebuilds a playable URL from an Xtream locator. Non-locators pass through unchanged.</summary>
    public static string? Resolve(string url, XtreamUrls urls)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(urls);
        if (!IsLocator(url))
        {
            return url;
        }

        var rest = url[Prefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return null;
        }

        var kind = rest[..slash];
        var file = rest[(rest.LastIndexOf('/') + 1)..];
        return StreamKinds.Contains(kind, StringComparer.Ordinal) && file.Length > 0
            ? urls.Stream(kind, Uri.UnescapeDataString(file))
            : null;
    }

    /// <summary>For opaque locators: the item id, or null.</summary>
    public static string? OpaqueId(string url) =>
        url.StartsWith($"{Prefix}{OpaqueHost}/", StringComparison.OrdinalIgnoreCase) ? url[$"{Prefix}{OpaqueHost}/".Length..] : null;
}
