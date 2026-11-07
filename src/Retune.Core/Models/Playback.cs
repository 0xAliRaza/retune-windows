namespace Retune.Core.Models;

/// <summary>What the player plays and the history stores.</summary>
/// <param name="Url">A stream URL, or a credential-free <c>retune://</c> locator in stored history.</param>
/// <param name="StreamId">Live channels: the id used for EPG and recents.</param>
/// <param name="ParentId">Episodes: the series id.</param>
public sealed record PlaybackItem(
    string Id,
    ContentKind Kind,
    string Title,
    string? Subtitle,
    string Url,
    Uri? Artwork = null,
    string? StreamId = null,
    string? ParentId = null)
{
    public bool IsLive => Kind == ContentKind.Live;
}

/// <summary>A resume point or watched mark.</summary>
public sealed record WatchProgress(PlaybackItem Item, double Position, double Duration, DateTimeOffset UpdatedAt)
{
    /// <summary>Below this many seconds a start doesn't count as "in progress".</summary>
    public const double ResumableAfterSeconds = 30;

    /// <summary>An item counts as finished with less than this left…</summary>
    public const double FinishedWithinSeconds = 90;

    /// <summary>…or once this fraction has been watched.</summary>
    public const double FinishedFraction = 0.95;

    /// <summary>Resuming rewinds this much so the viewer regains context.</summary>
    public const double ResumeRewindSeconds = 5;

    public double Fraction => Duration <= 0 ? 0 : Math.Clamp(Position / Duration, 0, 1);

    public bool IsFinished => Duration > 0 && (Position >= FinishedFraction * Duration || Duration - Position < FinishedWithinSeconds);

    public bool IsResumable => Position >= ResumableAfterSeconds && !IsFinished;

    public double Remaining => Math.Max(Duration - Position, 0);

    /// <summary>Where to start when resuming, or null when the item isn't resumable.</summary>
    public double? ResumePosition => IsResumable ? Math.Max(Position - ResumeRewindSeconds, 0) : null;
}
