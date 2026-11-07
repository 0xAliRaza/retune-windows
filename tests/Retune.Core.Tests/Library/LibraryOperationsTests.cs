using Microsoft.Extensions.Time.Testing;
using Retune.Core.Library;
using Retune.Core.Models;

namespace Retune.Core.Tests.Library;

public sealed class LibraryOperationsTests
{
    private static readonly Guid P1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static PlaybackItem Movie(string id) => new(id, ContentKind.Movie, $"Movie {id}", null, $"retune://movie/{id}.mp4");

    private static PlaybackItem Episode(string id, string series) => new(id, ContentKind.Episode, "Show", $"S1 E{id}", $"retune://series/{id}.mp4", ParentId: series);

    private static string Key(Guid playlist, ContentKind kind, string id) => LibraryKeys.Content(playlist, kind, id);

    [Fact]
    public void Favorites_toggle_and_are_scoped_by_kind()
    {
        var key = Key(P1, ContentKind.Movie, "7");
        var state = LibraryState.Empty.ToggleFavorite(key);

        Assert.True(state.IsFavorite(key));
        Assert.Equal(["7"], state.FavoriteIds(P1, ContentKind.Movie));
        Assert.Empty(state.FavoriteIds(P1, ContentKind.Series));
        Assert.Empty(state.FavoriteIds(P2, ContentKind.Movie));
        Assert.False(state.ToggleFavorite(key).IsFavorite(key));
    }

    [Fact]
    public void Recent_channels_dedupe_and_cap_at_30()
    {
        var state = LibraryState.Empty;
        for (var i = 0; i < 40; i++)
        {
            state = state.RecordRecentChannel(Key(P1, ContentKind.Live, $"k{i}"));
        }

        state = state.RecordRecentChannel(Key(P1, ContentKind.Live, "k35"));

        Assert.Equal(30, state.RecentChannels.Count);
        Assert.Equal("k35", state.RecentChannelIds(P1)[0]);
        Assert.Single(state.RecentChannels, k => k.EndsWith("/k35", StringComparison.Ordinal));
        Assert.Empty(state.RecentChannelIds(P2));
    }

    [Fact]
    public void Remove_all_clears_only_that_playlist()
    {
        var state = LibraryState.Empty
            .ToggleFavorite(Key(P1, ContentKind.Movie, "1"))
            .ToggleFavorite(Key(P2, ContentKind.Movie, "2"))
            .RecordRecentChannel(Key(P1, ContentKind.Live, "3"))
            .RecordProgress(Key(P1, ContentKind.Movie, "4"), Movie("4"), 600, 6000, T0)
            .ToggleHidden(LibraryKeys.Category(P1, LibrarySection.Live, "c"))
            .SetLocked(LibraryKeys.Category(P1, LibrarySection.Live, "d"), true)
            .TogglePinned(LibraryKeys.Category(P1, LibrarySection.Live, "e"))
            .WithPinHash("x");

        state = state.RemoveAll(P1);

        Assert.Equal([Key(P2, ContentKind.Movie, "2")], state.Favorites);
        Assert.Empty(state.RecentChannels);
        Assert.Empty(state.Progress);
        Assert.Empty(state.HiddenCategories);
        Assert.Empty(state.LockedCategories);
        Assert.Empty(state.PinnedCategories);
        Assert.Equal("x", state.PinHash);
    }

    [Fact]
    public void Continue_watching_is_scoped_sorted_and_collapses_series()
    {
        var state = LibraryState.Empty
            .RecordProgress(Key(P1, ContentKind.Movie, "m1"), Movie("m1"), 600, 6000, T0.AddMinutes(1))
            .RecordProgress(Key(P1, ContentKind.Episode, "e1"), Episode("e1", "s"), 600, 3000, T0.AddMinutes(2))
            .RecordProgress(Key(P1, ContentKind.Episode, "e2"), Episode("e2", "s"), 600, 3000, T0.AddMinutes(3))
            .RecordProgress(Key(P1, ContentKind.Movie, "done"), Movie("done"), 5990, 6000, T0.AddMinutes(4))
            .RecordProgress(Key(P2, ContentKind.Movie, "other"), Movie("other"), 600, 6000, T0.AddMinutes(5));

        var list = state.ContinueWatching(P1);

        Assert.Equal(["e2", "m1"], list.Select(e => e.Progress.Item.Id));
    }

    [Fact]
    public void Removing_an_episode_from_continue_watching_clears_its_series_but_keeps_watched_marks()
    {
        var state = LibraryState.Empty
            .MarkWatched(Key(P1, ContentKind.Episode, "e0"), Episode("e0", "s"), 3000, T0)
            .RecordProgress(Key(P1, ContentKind.Episode, "e1"), Episode("e1", "s"), 600, 3000, T0.AddMinutes(1))
            .RecordProgress(Key(P1, ContentKind.Episode, "e2"), Episode("e2", "s"), 600, 3000, T0.AddMinutes(2))
            .RecordProgress(Key(P1, ContentKind.Episode, "x1"), Episode("x1", "other"), 600, 3000, T0.AddMinutes(3));

        state = state.RemoveFromContinueWatching(Key(P1, ContentKind.Episode, "e2"));

        Assert.Equal(["e0", "x1"], state.Progress.Values.Select(p => p.Item.Id).Order());
        Assert.True(state.IsWatched(Key(P1, ContentKind.Episode, "e0")));
    }

    [Fact]
    public void Mark_watched_and_unwatched()
    {
        var key = Key(P1, ContentKind.Movie, "1");
        var watched = LibraryState.Empty.MarkWatched(key, Movie("1"), duration: null, T0);

        Assert.True(watched.IsWatched(key));
        Assert.Empty(watched.ContinueWatching(P1));
        Assert.Null(watched.MarkUnwatched(key).ProgressFor(key));
    }

    [Fact]
    public void Clearing_history_is_scoped_and_keeps_watched_marks()
    {
        var state = LibraryState.Empty
            .MarkWatched(Key(P1, ContentKind.Movie, "w"), Movie("w"), 100, T0)
            .RecordProgress(Key(P1, ContentKind.Movie, "r"), Movie("r"), 600, 6000, T0)
            .RecordProgress(Key(P1, ContentKind.Movie, "barely"), Movie("barely"), 10, 6000, T0)
            .RecordProgress(Key(P2, ContentKind.Movie, "o"), Movie("o"), 600, 6000, T0)
            .RecordRecentChannel(Key(P1, ContentKind.Live, "1"))
            .RecordRecentChannel(Key(P1, ContentKind.Live, "2"));

        state = state.ClearContinueWatching(P1).RemoveRecentChannel(Key(P1, ContentKind.Live, "1"));

        Assert.Equal(["barely", "o", "w"], state.Progress.Values.Select(p => p.Item.Id).Order());
        Assert.Equal(["2"], state.RecentChannelIds(P1));
        Assert.Empty(state.ClearRecentChannels(P1).RecentChannels);
    }

    [Fact]
    public void Pruning_keeps_watched_marks_over_in_progress_entries()
    {
        var state = LibraryState.Empty.MarkWatched(Key(P1, ContentKind.Movie, "old-watched"), Movie("w"), 100, T0.AddDays(-30));
        for (var i = 0; i < LibraryState.ProgressLimit; i++)
        {
            state = state.RecordProgress(Key(P1, ContentKind.Movie, $"p{i}"), Movie($"p{i}"), 600, 6000, T0.AddSeconds(i));
        }

        Assert.Equal(LibraryState.ProgressLimit, state.Progress.Count);
        Assert.True(state.IsWatched(Key(P1, ContentKind.Movie, "old-watched")));
        Assert.Null(state.ProgressFor(Key(P1, ContentKind.Movie, "p0")));
        Assert.NotNull(state.ProgressFor(Key(P1, ContentKind.Movie, "p1")));
    }

    [Theory]
    [InlineData(600, 9e18)]
    [InlineData(600, 0)]
    [InlineData(600, -5)]
    [InlineData(double.NaN, 6000)]
    [InlineData(600, double.PositiveInfinity)]
    [InlineData(600, 604_800)]
    public void Implausible_reports_are_not_saved(double position, double duration)
    {
        var state = LibraryState.Empty.RecordProgress(Key(P1, ContentKind.Movie, "1"), Movie("1"), position, duration, T0);

        Assert.Empty(state.Progress);
    }

    [Fact]
    public void Sanitize_rewrites_stored_stream_urls_once()
    {
        var key = Key(P1, ContentKind.Episode, "42");
        var leaked = new PlaybackItem("42", ContentKind.Episode, "Show", null, "http://h:8080/series/u/secret/42.mkv", ParentId: "s");
        var state = LibraryState.Empty.RecordProgress(key, leaked, 600, 3000, T0);

        var (first, changed) = state.SanitizeStreamUrls();
        var (_, changedAgain) = first.SanitizeStreamUrls();

        Assert.True(changed);
        Assert.Equal("retune://series/42.mkv", first.ProgressFor(key)!.Item.Url);
        Assert.False(changedAgain);
    }

    [Fact]
    public void Hidden_and_pinned_toggle_and_pins_keep_their_order()
    {
        var a = LibraryKeys.Category(P1, LibrarySection.Live, "a");
        var b = LibraryKeys.Category(P1, LibrarySection.Live, "b");
        var state = LibraryState.Empty.TogglePinned(b).TogglePinned(a).ToggleHidden(a);

        Assert.Equal([b, a], state.PinnedCategories);
        Assert.Contains(a, state.HiddenCategories);
        Assert.Empty(state.ToggleHidden(a).HiddenCategories);
        Assert.Equal([a], state.TogglePinned(b).PinnedCategories);
    }

    [Fact]
    public void Removing_the_pin_removes_every_lock()
    {
        var state = LibraryState.Empty.WithPinHash("h")
            .SetLocked(LibraryKeys.Category(P1, LibrarySection.Live, "a"), true)
            .SetLocked(LibraryKeys.Category(P2, LibrarySection.Movies, "b"), true);

        var cleared = state.RemovePin();

        Assert.Null(cleared.PinHash);
        Assert.Empty(cleared.LockedCategories);
    }

    [Fact]
    public void Progress_throttle_commits_at_most_every_30_seconds_except_final_reports()
    {
        var time = new FakeTimeProvider(T0);
        var throttle = new ProgressThrottle(time);

        Assert.True(throttle.ShouldCommit("k", isFinal: false));
        time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(throttle.ShouldCommit("k", isFinal: false));
        Assert.True(throttle.ShouldCommit("other", isFinal: false));
        Assert.True(throttle.ShouldCommit("k", isFinal: true));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(throttle.ShouldCommit("k", isFinal: false));
    }
}
