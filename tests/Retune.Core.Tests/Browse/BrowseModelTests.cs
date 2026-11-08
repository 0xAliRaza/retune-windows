using Retune.Core.Browse;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Search;

namespace Retune.Core.Tests.Browse;

public sealed class BrowseModelTests
{
    private static readonly Guid P = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static readonly Catalog Catalog = new(
        T0,
        LiveCategories: [new("news", " News "), new("kids", "Kids"), new("adult", "Adult")],
        VodCategories: [new("action", "Action"), new("drama", "Drama"), new("secret", "Secret")],
        SeriesCategories: [new("sitcom", "Sitcom")],
        Channels:
        [
            new("c1", 1, "BBC News", null, "news", null, "http://h/1.ts", false),
            new("c2", 2, "Cartoons", null, "kids", null, "http://h/2.ts", false),
            new("c3", 3, "Late Night", null, "adult", null, "http://h/3.ts", false),
            new("c4", 4, "Uncategorised", null, null, null, "http://h/4.ts", false),
        ],
        Movies:
        [
            new("m1", "Batman", null, "action", 7.5, null, "mp4", null),
            new("m2", "Amélie", null, "drama", 8.3, null, "mkv", null),
            new("m3", "Hidden Gem", null, "secret", 6.0, null, "mp4", null),
        ],
        Series: [new("s1", "Office", null, null, "sitcom", null, null, null, null, null)]);

    private static BrowseContext Context(LibraryState library, params string[] unlocked) =>
        new(P, CatalogIndex.Build(Catalog), library, unlocked.ToHashSet(StringComparer.Ordinal));

    private static string Cat(LibrarySection section, string id) => LibraryKeys.Category(P, section, id);

    private static IEnumerable<string> Ids(BrowseContent content) => ((ItemsContent)content).Items.Select(i => i.Id);

    [Fact]
    public void Hidden_and_locked_categories_are_excluded_from_all_and_search()
    {
        var library = LibraryState.Empty
            .ToggleHidden(Cat(LibrarySection.Live, "kids"))
            .SetLocked(Cat(LibrarySection.Live, "adult"), true);
        var context = Context(library);

        Assert.Equal(["c1", "c4"], Ids(BrowseModel.Content(context, LibrarySection.Live, CategoryFilter.All, null, BrowseSort.Provider)));
        Assert.Empty(Ids(BrowseModel.Content(context, LibrarySection.Live, CategoryFilter.All, "late", BrowseSort.Provider)));
    }

    [Fact]
    public void An_unlocked_category_appears_everywhere_again()
    {
        var context = Context(LibraryState.Empty.SetLocked(Cat(LibrarySection.Live, "adult"), true), Cat(LibrarySection.Live, "adult"));

        Assert.Contains("c3", Ids(BrowseModel.Content(context, LibrarySection.Live, CategoryFilter.All, null, BrowseSort.Provider)));
        Assert.False(BrowseModel.IsBlocked(context, LibrarySection.Live, "adult"));
        Assert.True(BrowseModel.IsLocked(context, LibrarySection.Live, "adult"));
    }

    [Fact]
    public void Selecting_a_locked_category_shows_the_prompt_unless_searching()
    {
        var context = Context(LibraryState.Empty.SetLocked(Cat(LibrarySection.Live, "adult"), true));

        Assert.Equal(new LockedContent("adult"), BrowseModel.Content(context, LibrarySection.Live, CategoryFilter.Category("adult"), null, BrowseSort.Provider));
        Assert.IsType<ItemsContent>(BrowseModel.Content(context, LibrarySection.Live, CategoryFilter.Category("adult"), "news", BrowseSort.Provider));
    }

    [Fact]
    public void A_chosen_category_ignores_exclusions_and_search_spans_every_category()
    {
        var context = Context(LibraryState.Empty.ToggleHidden(Cat(LibrarySection.Movies, "secret")));

        Assert.Equal(["m3"], Ids(BrowseModel.Content(context, LibrarySection.Movies, CategoryFilter.Category("secret"), null, BrowseSort.Provider)));
        Assert.Equal(["m2"], Ids(BrowseModel.Content(context, LibrarySection.Movies, CategoryFilter.Category("action"), "amelie", BrowseSort.Provider)));
    }

    [Fact]
    public void Favorites_follow_the_chosen_sort_not_the_order_added()
    {
        var library = LibraryState.Empty
            .ToggleFavorite(LibraryKeys.Content(P, ContentKind.Movie, "m2"))
            .ToggleFavorite(LibraryKeys.Content(P, ContentKind.Movie, "m1"));
        var context = Context(library);

        Assert.Equal(["m1", "m2"], Ids(BrowseModel.Content(context, LibrarySection.Movies, CategoryFilter.Favorites, null, BrowseSort.Provider)));
        Assert.Equal(["m2", "m1"], Ids(BrowseModel.Content(context, LibrarySection.Movies, CategoryFilter.Favorites, null, BrowseSort.Rating)));
    }

    [Fact]
    public void Recently_watched_keeps_recency_order_drops_unknown_and_excluded()
    {
        var library = LibraryState.Empty
            .RecordRecentChannel(LibraryKeys.Content(P, ContentKind.Live, "c1"))
            .RecordRecentChannel(LibraryKeys.Content(P, ContentKind.Live, "gone"))
            .RecordRecentChannel(LibraryKeys.Content(P, ContentKind.Live, "c3"))
            .RecordRecentChannel(LibraryKeys.Content(P, ContentKind.Live, "c2"))
            .SetLocked(Cat(LibrarySection.Live, "adult"), true);

        Assert.Equal(["c2", "c1"], Ids(BrowseModel.Content(Context(library), LibrarySection.Live, CategoryFilter.Recent, null, BrowseSort.Provider)));
    }

    [Fact]
    public void Continue_watching_is_filtered_by_section_and_excluded_categories()
    {
        var hiddenMovie = new PlaybackItem("m3", ContentKind.Movie, "Hidden Gem", null, "retune://movie/m3.mp4");
        var movie = new PlaybackItem("m1", ContentKind.Movie, "Batman", null, "retune://movie/m1.mp4");
        var unknown = new PlaybackItem("zz", ContentKind.Movie, "Gone", null, "retune://movie/zz.mp4");
        var episode = new PlaybackItem("e1", ContentKind.Episode, "Office", null, "retune://series/e1.mp4", ParentId: "s1");
        var library = LibraryState.Empty
            .RecordProgress(LibraryKeys.Content(P, ContentKind.Movie, "m3"), hiddenMovie, 600, 6000, T0.AddMinutes(1))
            .RecordProgress(LibraryKeys.Content(P, ContentKind.Movie, "m1"), movie, 600, 6000, T0.AddMinutes(2))
            .RecordProgress(LibraryKeys.Content(P, ContentKind.Movie, "zz"), unknown, 600, 6000, T0.AddMinutes(3))
            .RecordProgress(LibraryKeys.Content(P, ContentKind.Episode, "e1"), episode, 600, 3000, T0.AddMinutes(4))
            .ToggleHidden(Cat(LibrarySection.Movies, "secret"));
        var context = Context(library);

        Assert.Equal(["zz", "m1"], BrowseModel.ContinueWatching(context, LibrarySection.Movies).Select(e => e.Progress.Item.Id));
        Assert.Equal(["e1"], BrowseModel.ContinueWatching(context, LibrarySection.Series).Select(e => e.Progress.Item.Id));
        Assert.Empty(BrowseModel.ContinueWatching(context, LibrarySection.Live));
        Assert.Equal(CategoryFilter.ContinueWatching, BrowseModel.DefaultFilter(context, LibrarySection.Movies));
    }

    [Fact]
    public void Default_filter_skips_locked_categories_and_prefers_pins()
    {
        var locked = Context(LibraryState.Empty.SetLocked(Cat(LibrarySection.Movies, "action"), true));
        Assert.Equal(CategoryFilter.Category("drama"), BrowseModel.DefaultFilter(locked, LibrarySection.Movies));

        var pinned = Context(LibraryState.Empty.TogglePinned(Cat(LibrarySection.Movies, "secret")));
        Assert.Equal(CategoryFilter.Category("secret"), BrowseModel.DefaultFilter(pinned, LibrarySection.Movies));

        Assert.Equal(CategoryFilter.All, BrowseModel.DefaultFilter(locked, LibrarySection.Live));
    }

    [Fact]
    public void Browsable_categories_put_pins_first_and_drop_hidden()
    {
        var library = LibraryState.Empty
            .TogglePinned(Cat(LibrarySection.Live, "adult"))
            .TogglePinned(Cat(LibrarySection.Live, "news"))
            .ToggleHidden(Cat(LibrarySection.Live, "kids"));

        var (pins, others) = BrowseModel.BrowsableCategories(Context(library), LibrarySection.Live);

        Assert.Equal(["adult", "news"], pins.Select(c => c.Id));
        Assert.Empty(others);
    }

    [Fact]
    public void Hidden_categories_list_names_or_a_fallback()
    {
        var library = LibraryState.Empty
            .ToggleHidden(Cat(LibrarySection.Live, "news"))
            .ToggleHidden(Cat(LibrarySection.Movies, "999"));

        var hidden = BrowseModel.HiddenCategories(Context(library));

        Assert.Equal([new HiddenCategory(LibrarySection.Live, "news", "News"), new HiddenCategory(LibrarySection.Movies, "999", "Category 999")], hidden);
    }

    [Fact]
    public void Titles()
    {
        var context = Context(LibraryState.Empty);

        Assert.Equal("All Live TV", BrowseModel.Title(context, LibrarySection.Live, CategoryFilter.All, null));
        Assert.Equal("News", BrowseModel.Title(context, LibrarySection.Live, CategoryFilter.Category("news"), null));
        Assert.Equal("Live TV", BrowseModel.Title(context, LibrarySection.Live, CategoryFilter.Category("missing"), null));
        Assert.Equal("Results for “bbc”", BrowseModel.Title(context, LibrarySection.Live, CategoryFilter.Favorites, "  bbc "));
        Assert.Equal("Recently Watched", BrowseModel.Title(context, LibrarySection.Live, CategoryFilter.Recent, ""));
    }
    [Fact]
    public void Home_is_empty_for_a_new_library()
    {
        Assert.Empty(BrowseModel.HomeRows(Context(LibraryState.Empty), 20));
    }

    [Fact]
    public void Home_rows_come_in_order_without_locked_or_hidden_items()
    {
        string Key(ContentKind kind, string id) => LibraryKeys.Content(P, kind, id);
        var batman = new PlaybackItem("m1", ContentKind.Movie, "Batman", null, "http://h/m1.mp4");
        var gem = new PlaybackItem("m3", ContentKind.Movie, "Hidden Gem", null, "http://h/m3.mp4");
        var library = LibraryState.Empty
            .SetLocked(Cat(LibrarySection.Live, "adult"), true)
            .ToggleHidden(Cat(LibrarySection.Movies, "secret"))
            .RecordProgress(Key(ContentKind.Movie, "m1"), batman, 600, 6000, T0)
            .RecordProgress(Key(ContentKind.Movie, "m3"), gem, 600, 6000, T0.AddMinutes(1))
            .RecordRecentChannel(Key(ContentKind.Live, "c2"))
            .RecordRecentChannel(Key(ContentKind.Live, "c3"))
            .ToggleFavorite(Key(ContentKind.Live, "c1"))
            .ToggleFavorite(Key(ContentKind.Live, "c3"))
            .ToggleFavorite(Key(ContentKind.Movie, "m2"))
            .ToggleFavorite(Key(ContentKind.Series, "s1"));

        var rows = BrowseModel.HomeRows(Context(library), 20);

        Assert.Equal(
            [HomeRowKind.ContinueWatching, HomeRowKind.RecentChannels, HomeRowKind.FavoriteChannels, HomeRowKind.FavoriteMovies, HomeRowKind.FavoriteSeries],
            rows.Select(r => r.Kind));
        Assert.Equal(["m1"], rows[0].Entries.Cast<KeyedProgress>().Select(e => e.Progress.Item.Id));
        Assert.Equal(["c2"], rows[1].Entries.Cast<IBrowsable>().Select(i => i.Id));
        Assert.Equal(["c1"], rows[2].Entries.Cast<IBrowsable>().Select(i => i.Id));
        Assert.Equal(["m2"], rows[3].Entries.Cast<IBrowsable>().Select(i => i.Id));
        Assert.Equal(["s1"], rows[4].Entries.Cast<IBrowsable>().Select(i => i.Id));
        Assert.Equal((LibrarySection.Live, CategoryFilter.Favorites), (rows[2].Section, rows[2].Filter));
    }

    [Fact]
    public void Home_rows_are_capped_but_keep_the_total()
    {
        var library = LibraryState.Empty;
        foreach (var id in new[] { "c1", "c2", "c4" })
        {
            library = library.ToggleFavorite(LibraryKeys.Content(P, ContentKind.Live, id));
        }

        var row = Assert.Single(BrowseModel.HomeRows(Context(library), 2));
        Assert.Equal(2, row.Entries.Count);
        Assert.Equal(3, row.Total);
    }
}