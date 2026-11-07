using Retune.Core.Playback;

namespace Retune.Presentation.Player;

/// <summary>What the Windows media flyout shows.</summary>
public enum MediaSessionStatus
{
    Closed,
    Changing,
    Playing,
    Paused,
}

/// <summary>
/// The player's state as the system media controls (media keys, the volume flyout) should show it
/// (PLAN decision 4). Pure, so the mapping is tested without Windows.
/// </summary>
/// <param name="CanPause">The play/pause button works (at the end of a file it restarts it).</param>
public sealed record MediaSessionInfo(MediaSessionStatus Status, string Title, string? Subtitle, bool CanPrevious, bool CanNext, bool CanPause)
{
    public static MediaSessionInfo Closed { get; } = new(MediaSessionStatus.Closed, string.Empty, null, false, false, false);

    public static MediaSessionInfo From(PlaybackState state, bool presented, string? subtitle)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!presented || state.Item is not { } item || state.Phase == PlaybackPhase.Idle)
        {
            return Closed;
        }

        var status = state.Phase switch
        {
            PlaybackPhase.Playing => state.Paused ? MediaSessionStatus.Paused : MediaSessionStatus.Playing,
            PlaybackPhase.Ended or PlaybackPhase.Failed => MediaSessionStatus.Paused,
            _ => MediaSessionStatus.Changing,
        };
        return new MediaSessionInfo(status, item.Title, subtitle, state.CanGoPrevious, state.CanGoNext, CanPause: state.Phase is PlaybackPhase.Playing or PlaybackPhase.Ended);
    }
}
