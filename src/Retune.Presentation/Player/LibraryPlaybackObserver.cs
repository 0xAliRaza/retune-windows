using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Presentation.Services;

namespace Retune.Presentation.Player;

/// <summary>Connects the playback rules to the library: progress, recents and resume points for the active playlist.</summary>
public sealed class LibraryPlaybackObserver(LibraryService library, PlaylistController playlists) : IPlaybackObserver
{
    public void ReportProgress(PlaybackItem item, double position, double duration, bool isFinal)
    {
        if (playlists.Active is { } active)
        {
            library.RecordProgress(active.Id, item, position, duration, isFinal, hasAccount: active.Source is XtreamSource);
        }
    }

    public void LiveChannelStarted(PlaybackItem item)
    {
        if (playlists.Active is { } active)
        {
            library.RecordRecentChannel(active.Id, item);
        }
    }

    public double? ResumePositionFor(PlaybackItem item) =>
        playlists.Active is { } active ? library.State.ProgressFor(LibraryKeys.For(active.Id, item))?.ResumePosition : null;
}

/// <summary>Keeps the display awake while video is playing.</summary>
public interface IKeepAwake
{
    void KeepAwake(bool awake);
}
