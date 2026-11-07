using Retune.Core.Models;

namespace Retune.Core.Playback;

/// <summary>An ordered list of playable items and the one to start with.</summary>
public sealed record Lineup(IReadOnlyList<PlaybackItem> Items, int StartIndex, string Title)
{
    /// <summary>Queues hold at most this many items around the selection (a 65k "All" view needs no 65k URLs).</summary>
    public const int WindowSize = 500;

    /// <summary>The slice of <paramref name="count"/> items centred on <paramref name="index"/>.</summary>
    public static (int Start, int Length) Window(int count, int index, int size = WindowSize)
    {
        if (count <= size)
        {
            return (0, count);
        }

        return (Math.Clamp(index - (size / 2), 0, count - size), size);
    }

    /// <summary>
    /// Builds a queue from the list something was picked from. Unplayable elements are skipped and the start
    /// index follows the selected element. Null when the selected element itself can't be played.
    /// </summary>
    public static Lineup? Build<T>(IReadOnlyList<T> source, int selectedIndex, Func<T, PlaybackItem?> toItem, string title)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(toItem);
        if (selectedIndex < 0 || selectedIndex >= source.Count)
        {
            return null;
        }

        var (start, length) = Window(source.Count, selectedIndex);
        var items = new List<PlaybackItem>(length);
        int? startIndex = null;
        for (var i = start; i < start + length; i++)
        {
            if (toItem(source[i]) is not { } item)
            {
                continue;
            }

            if (i == selectedIndex)
            {
                startIndex = items.Count;
            }

            items.Add(item);
        }

        return startIndex is { } s ? new Lineup(items, s, title) : null;
    }

    /// <summary>Live queues wrap like a TV remote; on-demand queues stop at the ends (null).</summary>
    public static int? Target(int index, int delta, int count, bool wraps)
    {
        if (count <= 0)
        {
            return null;
        }

        var target = index + delta;
        if (wraps)
        {
            return ((target % count) + count) % count;
        }

        return target >= 0 && target < count ? target : null;
    }

    /// <summary>Autoplay only rolls from an episode into the next episode of the same series.</summary>
    public static bool ShouldAutoplay(PlaybackItem current, PlaybackItem? next, bool enabled) =>
        enabled
        && next is not null
        && current.Kind == ContentKind.Episode
        && next.Kind == ContentKind.Episode
        && current.ParentId is { } series
        && next.ParentId == series;
}

public enum EpisodeAction
{
    Start,
    Resume,
    Next,
}

/// <summary>What the series page's main button does, and to which episode (index in play order).</summary>
public sealed record EpisodePick(EpisodeAction Action, int Index)
{
    /// <summary>Follows the most recent real activity: resume it, play the one after it, or start over.</summary>
    public static EpisodePick? Choose(int count, Func<int, WatchProgress?> progressAt)
    {
        ArgumentNullException.ThrowIfNull(progressAt);
        if (count <= 0)
        {
            return null;
        }

        int? latest = null;
        DateTimeOffset latestAt = default;
        for (var i = 0; i < count; i++)
        {
            if (progressAt(i) is { } p && (p.IsResumable || p.IsFinished) && (latest is null || p.UpdatedAt > latestAt))
            {
                latest = i;
                latestAt = p.UpdatedAt;
            }
        }

        if (latest is not { } index)
        {
            return new EpisodePick(EpisodeAction.Start, 0);
        }

        if (progressAt(index)!.IsResumable)
        {
            return new EpisodePick(EpisodeAction.Resume, index);
        }

        return index + 1 < count ? new EpisodePick(EpisodeAction.Next, index + 1) : new EpisodePick(EpisodeAction.Start, 0);
    }

    /// <param name="anyWatched">Whether any episode of the series is watched ("Watch Again").</param>
    public string Label(Episode episode, bool anyWatched)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var code = $"S{episode.Season} E{episode.Number}";
        return Action switch
        {
            EpisodeAction.Resume => $"Resume {code}",
            EpisodeAction.Next => $"Play Next · {code}",
            _ => anyWatched ? "Watch Again" : $"Play {code}",
        };
    }
}
