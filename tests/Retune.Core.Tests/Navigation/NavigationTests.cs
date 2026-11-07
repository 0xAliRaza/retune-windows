using Retune.Core.Models;
using Retune.Core.Navigation;

namespace Retune.Core.Tests.Navigation;

public sealed class NavigationTests
{
    [Theory]
    [InlineData(1000, 150, 20, 6)]
    [InlineData(999, 150, 20, 5)]
    [InlineData(100, 150, 20, 1)]
    [InlineData(500, 0, 20, 1)]
    public void Columns(double width, double minimum, double spacing, int expected)
    {
        Assert.Equal(expected, GridNavigation.Columns(width, minimum, spacing));
    }

    [Theory]
    [InlineData(null, Direction.Down, 0)]
    [InlineData(5, Direction.Right, 6)]
    [InlineData(5, Direction.Left, 4)]
    [InlineData(5, Direction.Up, 1)]
    [InlineData(1, Direction.Up, 1)]
    [InlineData(5, Direction.Down, 9)]
    [InlineData(4, Direction.Down, 8)]
    [InlineData(9, Direction.Down, 9)]
    [InlineData(0, Direction.Left, 0)]
    [InlineData(9, Direction.Right, 9)]
    [InlineData(6, Direction.Down, 9)]
    public void Moves_in_a_four_column_grid_of_ten(int? from, Direction direction, int expected)
    {
        Assert.Equal(expected, GridNavigation.Move(from, direction, count: 10, columns: 4));
    }

    [Fact]
    public void Empty_grid_has_no_focus()
    {
        Assert.Null(GridNavigation.Move(3, Direction.Down, count: 0, columns: 4));
    }

    [Theory]
    [InlineData(2, Direction.Up, true)]
    [InlineData(4, Direction.Up, false)]
    [InlineData(4, Direction.Left, true)]
    [InlineData(5, Direction.Left, false)]
    [InlineData(0, Direction.Down, false)]
    [InlineData(3, Direction.Right, false)]
    public void Edges_let_focus_leave_the_grid(int index, Direction direction, bool atEdge)
    {
        Assert.Equal(atEdge, GridNavigation.IsAtEdge(index, direction, columns: 4));
    }

    [Fact]
    public void Pane_rules()
    {
        Assert.Equal(PaneAction.Focus(Pane.Categories), PaneNavigation.Action(Pane.Sidebar, PaneKey.Tab, false, false));
        Assert.Equal(PaneAction.Focus(Pane.Categories), PaneNavigation.Action(Pane.Sidebar, PaneKey.Enter, false, false));
        Assert.Equal(PaneAction.Focus(Pane.Content), PaneNavigation.Action(Pane.Sidebar, PaneKey.ShiftTab, false, false));
        Assert.Equal(PaneAction.PassThrough, PaneNavigation.Action(Pane.Sidebar, PaneKey.Left, false, false));

        Assert.Equal(PaneAction.Focus(Pane.Content), PaneNavigation.Action(Pane.Categories, PaneKey.Right, false, false));
        Assert.Equal(PaneAction.Focus(Pane.Sidebar), PaneNavigation.Action(Pane.Categories, PaneKey.Escape, false, false));

        Assert.Equal(PaneAction.Back, PaneNavigation.Action(Pane.Content, PaneKey.Escape, detailShown: true, false));
        Assert.Equal(PaneAction.Focus(Pane.Categories), PaneNavigation.Action(Pane.Content, PaneKey.Escape, detailShown: false, false));
        Assert.Equal(PaneAction.Focus(Pane.Sidebar), PaneNavigation.Action(Pane.Content, PaneKey.Tab, false, false));
        Assert.Equal(PaneAction.PassThrough, PaneNavigation.Action(Pane.Content, PaneKey.Left, false, contentUsesLeft: true));
        Assert.Equal(PaneAction.Focus(Pane.Categories), PaneNavigation.Action(Pane.Content, PaneKey.Left, false, contentUsesLeft: false));
        Assert.Equal(PaneAction.PassThrough, PaneNavigation.Action(Pane.Content, PaneKey.Enter, false, false));
    }
}

public sealed class ModelTests
{
    [Fact]
    public void Epg_now_and_next()
    {
        var t0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var a = new EpgEntry("A", null, t0, t0.AddHours(1));
        var b = new EpgEntry("B", null, t0.AddHours(1), t0.AddHours(2));
        var c = new EpgEntry("C", null, t0.AddHours(3), t0.AddHours(4));

        var during = NowNext.At([c, b, a], t0.AddMinutes(30));
        Assert.Equal(a, during.Now);
        Assert.Equal(b, during.Next);
        Assert.Equal(0.5, a.Progress(t0.AddMinutes(30)), 3);

        var gap = NowNext.At([a, b, c], t0.AddMinutes(150));
        Assert.Null(gap.Now);
        Assert.Equal(c, gap.Next);
        Assert.Equal($"{t0.ToUnixTimeSeconds()}-A", a.Id);
    }

    [Fact]
    public void Epg_refetches_when_the_programme_changes_within_bounds()
    {
        var t0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        EpgEntry[] guide = [new("A", null, t0, t0.AddMinutes(30)), new("B", null, t0.AddHours(2), t0.AddHours(3))];

        Assert.Equal(TimeSpan.FromMinutes(20), EpgRefresh.Delay(guide, t0.AddMinutes(10)));
        Assert.Equal(TimeSpan.FromMinutes(60), EpgRefresh.Delay(guide, t0.AddMinutes(60)));
        Assert.Equal(EpgRefresh.MinDelay, EpgRefresh.Delay(guide, t0.AddMinutes(29.9)));
        Assert.Equal(EpgRefresh.MaxDelay, EpgRefresh.Delay([new("Long", null, t0, t0.AddDays(2))], t0));
        Assert.Null(EpgRefresh.Delay([], t0));
        Assert.Null(EpgRefresh.Delay(guide, t0.AddHours(5)));
    }

    [Fact]
    public void Epg_progress_of_a_zero_length_entry_is_zero()
    {
        var t = DateTimeOffset.UnixEpoch;
        Assert.Equal(0, new EpgEntry("X", null, t, t).Progress(t));
    }

    [Theory]
    [InlineData("2025-06-20", "2025")]
    [InlineData("199", null)]
    [InlineData("abcd-01", null)]
    [InlineData(null, null)]
    public void Year_comes_from_the_release_date(string? date, string? year)
    {
        Assert.Equal(year, new MediaInfo(ReleaseDate: date).Year);
    }

    [Fact]
    public void Catalog_staleness_is_strictly_greater()
    {
        var fetched = DateTimeOffset.FromUnixTimeSeconds(1000);
        var catalog = new Catalog(fetched, [], [], [], [], [], []);

        Assert.True(catalog.IsStale(fetched.AddSeconds(61), TimeSpan.FromSeconds(60)));
        Assert.False(catalog.IsStale(fetched.AddSeconds(59), TimeSpan.FromSeconds(60)));
        Assert.True(catalog.IsEmpty);
    }

    [Fact]
    public void Playlist_kind_labels_and_series_support()
    {
        var created = DateTimeOffset.UnixEpoch;
        Assert.Equal("Xtream Codes", new Playlist(Guid.NewGuid(), "X", new XtreamSource("http://h", "u"), created).KindLabel);
        Assert.Equal("M3U URL", new Playlist(Guid.NewGuid(), "M", new M3uSource("https://h/list.m3u"), created).KindLabel);
        Assert.Equal("M3U File", new Playlist(Guid.NewGuid(), "F", new M3uSource(@"C:\lists\tv.m3u"), created).KindLabel);
        Assert.False(new Playlist(Guid.NewGuid(), "M", new M3uSource("https://h/list.m3u"), created).SupportsSeries);
    }

    [Fact]
    public void Account_is_active_ignoring_case()
    {
        Assert.True(new AccountInfo("ACTIVE").IsActive);
        Assert.False(new AccountInfo("Expired").IsActive);
        Assert.Empty(new AccountInfo().Formats);
    }
}
