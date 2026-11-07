using System.Text;
using Retune.Core.Models;
using Retune.Core.Policies;

namespace Retune.Core.Xtream;

/// <summary>Builds Xtream API and stream URLs for one account.</summary>
public sealed class XtreamUrls(XtreamCredentials credentials)
{
    internal const string LivePath = "live";
    internal const string MoviePath = "movie";
    internal const string SeriesPath = "series";

    private const string DefaultVodExtension = "mp4";

    /// <summary>The server with exactly one trailing slash removed, as the original does.</summary>
    private string Base => credentials.Server.EndsWith('/') ? credentials.Server[..^1] : credentials.Server;

    /// <summary>
    /// <c>&lt;server&gt;/player_api.php?username=…&amp;password=…[&amp;action=…][&amp;extra sorted by key]</c>.
    /// </summary>
    public string Api(string? action = null, IReadOnlyDictionary<string, string>? extra = null)
    {
        var query = new StringBuilder()
            .Append("username=").Append(PercentEncoding.QueryValue(credentials.Username))
            .Append("&password=").Append(PercentEncoding.QueryValue(credentials.Password));
        if (action is not null)
        {
            query.Append("&action=").Append(PercentEncoding.QueryValue(action));
        }

        foreach (var (key, value) in (extra ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            query.Append('&').Append(key).Append('=').Append(PercentEncoding.QueryValue(value));
        }

        return $"{Base}/player_api.php?{query}";
    }

    /// <param name="extension"><c>ts</c> or <c>m3u8</c>, from <see cref="LiveFormats.Extension"/>.</param>
    public string Live(string streamId, string extension) => Stream(LivePath, $"{streamId}.{extension}");

    public string Movie(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return movie.DirectUrl ?? Stream(MoviePath, $"{movie.Id}.{movie.ContainerExtension ?? DefaultVodExtension}");
    }

    public string Episode(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return episode.DirectUrl ?? Stream(SeriesPath, $"{episode.Id}.{episode.ContainerExtension ?? DefaultVodExtension}");
    }

    /// <summary><c>&lt;base&gt;/&lt;kind&gt;/&lt;user&gt;/&lt;password&gt;/&lt;file&gt;</c>, each segment encoded including '/'.</summary>
    internal string Stream(string kindPath, string file) =>
        $"{Base}/{kindPath}/{PercentEncoding.PathSegment(credentials.Username)}/{PercentEncoding.PathSegment(credentials.Password)}/{PercentEncoding.PathSegment(file)}";
}

/// <summary>Picks the live stream container.</summary>
public static class LiveFormats
{
    /// <summary>
    /// <c>ts</c> or <c>m3u8</c>. Automatic prefers MPEG-TS unless the account explicitly doesn't allow it
    /// (an empty list, as before authentication, also means <c>ts</c>).
    /// </summary>
    public static string Extension(LiveFormat preference, IReadOnlyList<string> allowedFormats)
    {
        ArgumentNullException.ThrowIfNull(allowedFormats);
        return preference switch
        {
            LiveFormat.Ts => "ts",
            LiveFormat.Hls => "m3u8",
            _ => allowedFormats.Count == 0 || allowedFormats.Contains("ts", StringComparer.OrdinalIgnoreCase) ? "ts" : "m3u8",
        };
    }
}
