namespace Retune.Core.Models;

/// <summary>One programme from the short EPG.</summary>
public sealed record EpgEntry(string Title, string? Summary, DateTimeOffset Start, DateTimeOffset End)
{
    public string Id => $"{Start.ToUnixTimeSeconds()}-{Title}";

    public bool IsLive(DateTimeOffset at) => Start <= at && at < End;

    /// <summary>How far through the programme <paramref name="at"/> is, clamped to 0–1.</summary>
    public double Progress(DateTimeOffset at)
    {
        var length = (End - Start).TotalSeconds;
        return length <= 0 ? 0 : Math.Clamp((at - Start).TotalSeconds / length, 0, 1);
    }
}

/// <summary>When to fetch the short EPG again for the channel that's playing.</summary>
public static class EpgRefresh
{
    public static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromHours(6);

    /// <summary>
    /// When the current programme ends, or when the next one starts if nothing is on now,
    /// clamped to 60 s – 6 h. Null when the guide has no times at all (don't refetch).
    /// </summary>
    public static TimeSpan? Delay(IReadOnlyList<EpgEntry> entries, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var guide = NowNext.At(entries, now);
        DateTimeOffset? at = guide.Now?.End ?? guide.Next?.Start;
        if (at is not { } when)
        {
            return null;
        }

        var wait = when - now;
        return wait < MinDelay ? MinDelay : wait > MaxDelay ? MaxDelay : wait;
    }
}

/// <summary>The "Now" and "Next" programmes shown in the player.</summary>
public sealed record NowNext(EpgEntry? Now, EpgEntry? Next)
{
    public static NowNext None { get; } = new(null, null);

    /// <param name="entries">Any order; they are sorted by start here.</param>
    public static NowNext At(IEnumerable<EpgEntry> entries, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.OrderBy(e => e.Start).ToList();
        var now = sorted.FirstOrDefault(e => e.IsLive(at));
        var next = now is not null
            ? sorted.FirstOrDefault(e => e.Start >= now.End)
            : sorted.FirstOrDefault(e => e.Start > at);
        return new NowNext(now, next);
    }
}
