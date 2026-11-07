using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Presentation.Player;

namespace Retune.Presentation.Tests;

public sealed class MediaSessionInfoTests
{
    private static readonly PlaybackItem Channel = new("c1", ContentKind.Live, "Nova News", "News", "http://127.0.0.1/live/1.ts", StreamId: "c1");

    private static PlaybackState Live(PlaybackPhase phase, bool paused = false) =>
        PlaybackState.Idle with { Phase = phase, Item = Channel, Index = 0, Count = 3, Wraps = true, Paused = paused };

    [Fact]
    public void Nothing_shows_when_the_player_is_closed_or_idle()
    {
        Assert.Equal(MediaSessionInfo.Closed, MediaSessionInfo.From(Live(PlaybackPhase.Playing), presented: false, null));
        Assert.Equal(MediaSessionInfo.Closed, MediaSessionInfo.From(PlaybackState.Idle, presented: true, null));
    }

    [Theory]
    [InlineData(PlaybackPhase.Playing, false, MediaSessionStatus.Playing, true)]
    [InlineData(PlaybackPhase.Playing, true, MediaSessionStatus.Paused, true)]
    [InlineData(PlaybackPhase.Tuning, false, MediaSessionStatus.Changing, false)]
    [InlineData(PlaybackPhase.Reconnecting, false, MediaSessionStatus.Changing, false)]
    [InlineData(PlaybackPhase.Ended, false, MediaSessionStatus.Paused, true)]
    [InlineData(PlaybackPhase.Failed, false, MediaSessionStatus.Paused, false)]
    public void Phases_map_to_flyout_states(PlaybackPhase phase, bool paused, MediaSessionStatus expected, bool canPause)
    {
        var info = MediaSessionInfo.From(Live(phase, paused), presented: true, "Now: Evening Bulletin");

        Assert.Equal(expected, info.Status);
        Assert.Equal(canPause, info.CanPause);
        Assert.Equal("Nova News", info.Title);
        Assert.Equal("Now: Evening Bulletin", info.Subtitle);
    }

    [Fact]
    public void Previous_and_next_follow_the_lineup()
    {
        var single = MediaSessionInfo.From(Live(PlaybackPhase.Playing) with { Count = 1 }, presented: true, null);
        Assert.False(single.CanPrevious);
        Assert.False(single.CanNext);

        var lastMovie = MediaSessionInfo.From(Live(PlaybackPhase.Playing) with { Wraps = false, Index = 2 }, presented: true, null);
        Assert.True(lastMovie.CanPrevious);
        Assert.False(lastMovie.CanNext);
    }
}
