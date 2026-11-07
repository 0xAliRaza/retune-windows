using Retune.Core.Models;

namespace Retune.Core.Playback;

public enum PlaybackPhase
{
    /// <summary>Nothing is playing.</summary>
    Idle,

    /// <summary>An item is chosen but no frame has shown yet (the tuning card is up).</summary>
    Tuning,

    /// <summary>Waiting before an automatic retry.</summary>
    Reconnecting,

    Playing,

    /// <summary>An on-demand item reached its real end; the last frame stays.</summary>
    Ended,

    /// <summary>Retries are exhausted or the item can't be played at all.</summary>
    Failed,
}

/// <summary>An immutable snapshot of the session for the player UI.</summary>
public sealed record PlaybackState(
    PlaybackPhase Phase,
    PlaybackItem? Item,
    int Index,
    int Count,
    bool Wraps,
    double Position,
    double Duration,
    bool Paused,
    bool Buffering,
    bool HasVideoFrame,
    int ReconnectAttempt,
    string? ErrorMessage,
    int? UpNextSecondsLeft,
    string? LineupTitle)
{
    public static PlaybackState Idle { get; } = new(PlaybackPhase.Idle, null, -1, 0, false, 0, 0, false, false, false, 0, null, null, null);

    public bool IsLive => Item?.IsLive == true;

    public bool CanGoPrevious => Count > 1 && (Wraps || Index > 0);

    public bool CanGoNext => Count > 1 && (Wraps || Index < Count - 1);

    public bool IsSeekable => !IsLive && Duration > 0;
}

/// <summary>Where the session reports things the library must remember. Called on the session thread.</summary>
public interface IPlaybackObserver
{
    /// <summary>
    /// Progress for an on-demand item: every 5 s while playing, and final on stop, switch and end.
    /// Only sent once a frame has shown and the duration is known.
    /// </summary>
    void ReportProgress(PlaybackItem item, double position, double duration, bool isFinal);

    /// <summary>A live channel started loading (for Recently Watched).</summary>
    void LiveChannelStarted(PlaybackItem item);

    /// <summary>The saved resume point for an on-demand item, if any.</summary>
    double? ResumePositionFor(PlaybackItem item);
}
