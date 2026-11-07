using Retune.Core.Models;
using Retune.Core.Playback;

namespace Retune.Core.Tests.Playback;

public sealed class LineupTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static PlaybackItem Item(string id) => new(id, ContentKind.Live, id, null, $"http://h/{id}.ts");

    private static PlaybackItem Ep(string id, string? series) => new(id, ContentKind.Episode, "Show", null, "u", ParentId: series);

    [Theory]
    [InlineData(1000, 500, 100, 450)]
    [InlineData(1000, 5, 100, 0)]
    [InlineData(1000, 990, 100, 900)]
    [InlineData(50, 10, 100, 0)]
    public void Windows_center_on_the_selection(int count, int index, int size, int expectedStart)
    {
        var (start, length) = Lineup.Window(count, index, size);

        Assert.Equal(expectedStart, start);
        Assert.Equal(Math.Min(count, size), length);
    }

    [Fact]
    public void Large_lists_are_windowed_to_500()
    {
        var source = Enumerable.Range(0, 65_000).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

        var queue = Lineup.Build(source, 30_000, Item, "All Movies");

        Assert.Equal(500, queue!.Items.Count);
        Assert.Equal("30000", queue.Items[queue.StartIndex].Id);
    }

    [Fact]
    public void Build_keeps_the_selection_aligned_when_items_are_skipped()
    {
        string[] source = ["a", "skip", "b", "c"];
        PlaybackItem? ToItem(string s) => s == "skip" ? null : Item(s);

        var queue = Lineup.Build(source, 2, ToItem, "T");

        Assert.Equal(["a", "b", "c"], queue!.Items.Select(i => i.Id));
        Assert.Equal("b", queue.Items[queue.StartIndex].Id);
        Assert.Null(Lineup.Build(source, 1, ToItem, "T"));
        Assert.Null(Lineup.Build(source, 9, ToItem, "T"));
    }

    [Fact]
    public void Live_wraps_and_on_demand_stops()
    {
        Assert.Equal(4, Lineup.Target(0, -1, 5, wraps: true));
        Assert.Equal(0, Lineup.Target(4, 1, 5, wraps: true));
        Assert.Null(Lineup.Target(0, -1, 5, wraps: false));
        Assert.Null(Lineup.Target(4, 1, 5, wraps: false));
        Assert.Equal(3, Lineup.Target(2, 1, 5, wraps: false));
        Assert.Null(Lineup.Target(0, 1, 0, wraps: true));
    }

    [Fact]
    public void Autoplay_only_within_the_same_series()
    {
        Assert.True(Lineup.ShouldAutoplay(Ep("a1", "a"), Ep("a2", "a"), enabled: true));
        Assert.False(Lineup.ShouldAutoplay(Ep("a1", "a"), Ep("a2", "a"), enabled: false));
        Assert.False(Lineup.ShouldAutoplay(Ep("a1", "a"), Ep("b1", "b"), enabled: true));
        Assert.False(Lineup.ShouldAutoplay(Ep("a1", "a"), null, enabled: true));
        Assert.False(Lineup.ShouldAutoplay(Ep("a1", null), Ep("a2", null), enabled: true));
        Assert.False(Lineup.ShouldAutoplay(new PlaybackItem("m", ContentKind.Movie, "M", null, "u"), Ep("a2", "a"), enabled: true));
    }

    private static WatchProgress P(double position, double duration, int minute) =>
        new(Ep("x", "s"), position, duration, T0.AddMinutes(minute));

    [Fact]
    public void Episode_picker_follows_the_latest_activity()
    {
        var progress = new Dictionary<int, WatchProgress> { [1] = P(600, 3000, 1), [3] = P(3000, 3000, 5) };
        Assert.Equal(new EpisodePick(EpisodeAction.Next, 4), EpisodePick.Choose(6, progress.GetValueOrDefault));

        progress[1] = P(700, 3000, 9);
        Assert.Equal(new EpisodePick(EpisodeAction.Resume, 1), EpisodePick.Choose(6, progress.GetValueOrDefault));

        var finale = new Dictionary<int, WatchProgress> { [5] = P(3000, 3000, 1) };
        Assert.Equal(new EpisodePick(EpisodeAction.Start, 0), EpisodePick.Choose(6, finale.GetValueOrDefault));

        var barely = new Dictionary<int, WatchProgress> { [2] = P(10, 3000, 1) };
        Assert.Equal(new EpisodePick(EpisodeAction.Start, 0), EpisodePick.Choose(6, barely.GetValueOrDefault));

        Assert.Null(EpisodePick.Choose(0, _ => null));
    }

    [Fact]
    public void Episode_picker_labels()
    {
        var episode = new Episode("1", 2, 3, "T");

        Assert.Equal("Resume S2 E3", new EpisodePick(EpisodeAction.Resume, 0).Label(episode, false));
        Assert.Equal("Play Next · S2 E3", new EpisodePick(EpisodeAction.Next, 0).Label(episode, false));
        Assert.Equal("Play S2 E3", new EpisodePick(EpisodeAction.Start, 0).Label(episode, anyWatched: false));
        Assert.Equal("Watch Again", new EpisodePick(EpisodeAction.Start, 0).Label(episode, anyWatched: true));
    }
}
