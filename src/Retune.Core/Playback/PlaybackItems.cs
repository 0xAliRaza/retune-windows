using Retune.Core.Models;
using Retune.Core.Xtream;

namespace Retune.Core.Playback;

/// <summary>
/// Turns catalog entries into playable items. Spec: docs/spec/core.md §1.13. Null means unplayable
/// (no URL can be built); such entries are skipped when lineups are built.
/// </summary>
public sealed class PlaybackItems(XtreamUrls? urls, string liveExtension, Func<string?, string?> categoryName)
{
    public PlaybackItem? Channel(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var url = channel.DirectUrl ?? urls?.Live(channel.Id, liveExtension);
        return url is null
            ? null
            : new PlaybackItem(channel.Id, ContentKind.Live, channel.Name, categoryName(channel.CategoryId)?.Trim(), url, channel.Logo, StreamId: channel.Id);
    }

    public PlaybackItem? Movie(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        var url = urls?.Movie(movie) ?? movie.DirectUrl;
        return url is null ? null : new PlaybackItem(movie.Id, ContentKind.Movie, movie.Name, null, url, movie.Poster);
    }

    /// <summary>Episodes need an Xtream account. The title is the series name; the subtitle is "S1 E2 · Title".</summary>
    public PlaybackItem? Episode(Series series, Episode episode)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episode);
        if (urls is null && episode.DirectUrl is null)
        {
            return null;
        }

        var url = episode.DirectUrl ?? urls!.Episode(episode);
        return new PlaybackItem(episode.Id, ContentKind.Episode, series.Name, $"S{episode.Season} E{episode.Number} · {episode.Title}", url, episode.Image ?? series.Cover, ParentId: series.Id);
    }

    /// <summary>
    /// A stored history item made playable again: locators resolve against the current account; M3U items
    /// are looked up in the catalog by id. Null when it can no longer be played.
    /// </summary>
    public PlaybackItem? Restore(PlaybackItem stored, Func<string, Movie?> findMovie)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(findMovie);
        if (!Locators.IsLocator(stored.Url))
        {
            return stored;
        }

        if (urls is not null)
        {
            return Locators.Resolve(stored.Url, urls) is { } resolved ? stored with { Url = resolved } : null;
        }

        return Locators.OpaqueId(stored.Url) is { } id && findMovie(id)?.DirectUrl is { } direct ? stored with { Url = direct } : null;
    }
}
