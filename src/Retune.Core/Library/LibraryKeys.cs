using Retune.Core.Models;

namespace Retune.Core.Library;

/// <summary>
/// Builds the string keys the library is stored under. Every key starts with the playlist prefix,
/// so removing a playlist removes its entries with one prefix match.
/// </summary>
public static class LibraryKeys
{
    /// <summary>"&lt;UUID&gt;/" with the UUID in canonical upper case.</summary>
    public static string PlaylistPrefix(Guid playlistId) => $"{playlistId.ToString("D").ToUpperInvariant()}/";

    /// <summary>"&lt;UUID&gt;/&lt;kind&gt;/&lt;id&gt;".</summary>
    public static string Content(Guid playlistId, ContentKind kind, string id) =>
        $"{PlaylistPrefix(playlistId)}{kind.ToKey()}/{id}";

    /// <summary>The key a playback item is stored under (recents use the stream id for live).</summary>
    public static string For(Guid playlistId, PlaybackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Content(playlistId, item.Kind, item.Kind == ContentKind.Live ? item.StreamId ?? item.Id : item.Id);
    }

    /// <summary>
    /// The favourite the player's star toggles: the channel or movie itself, or the series an episode belongs to
    /// (episodes aren't favourites on their own). Null when an episode doesn't know its series.
    /// </summary>
    public static string? FavoriteFor(Guid playlistId, PlaybackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            ContentKind.Episode => item.ParentId is { } series ? Content(playlistId, ContentKind.Series, series) : null,
            ContentKind.Series => null,
            _ => For(playlistId, item),
        };
    }

    /// <summary>"&lt;UUID&gt;/category/&lt;kind&gt;/".</summary>
    public static string CategoryPrefix(Guid playlistId, LibrarySection section) =>
        $"{PlaylistPrefix(playlistId)}category/{section.ToContentKind().ToKey()}/";

    /// <summary>"&lt;UUID&gt;/category/&lt;kind&gt;/&lt;categoryId&gt;".</summary>
    public static string Category(Guid playlistId, LibrarySection section, string categoryId) =>
        CategoryPrefix(playlistId, section) + categoryId;

    /// <summary>Recovers the category ids of one section from a set of category keys.</summary>
    public static IEnumerable<string> CategoryIds(IEnumerable<string> keys, Guid playlistId, LibrarySection section)
    {
        var prefix = CategoryPrefix(playlistId, section);
        return keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[prefix.Length..]);
    }
}
