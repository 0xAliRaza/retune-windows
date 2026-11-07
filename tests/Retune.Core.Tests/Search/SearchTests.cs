using System.Diagnostics;
using Retune.Core.Models;
using Retune.Core.Search;

namespace Retune.Core.Tests.Search;

public sealed class SearchTests
{
    private static Movie M(string id, string name, string? category, double? rating = null, long? added = null) =>
        new(id, name, null, category, rating, added is { } a ? DateTimeOffset.FromUnixTimeSeconds(a) : null, null, null);

    private static readonly BrowseIndex<Movie> Index = new(
    [
        M("1", "The Batman", "a", rating: 7.9, added: 300),
        M("2", "Amélie", "b", rating: 8.3, added: 100),
        M("3", "Batman Begins", "a", rating: null, added: 200),
    ]);

    [Fact]
    public void Counts_and_filters_by_category()
    {
        Assert.Equal(2, Index.Count("a"));
        Assert.Equal(0, Index.Count("missing"));
        Assert.Equal(["2"], Index.Select(new BrowseQuery(CategoryId: "b")).Select(m => m.Id));
        Assert.Equal(3, Index.Select(new BrowseQuery()).Count);
    }

    [Theory]
    [InlineData("amelie", new[] { "2" })]
    [InlineData("BATMAN", new[] { "1", "3" })]
    [InlineData("batman begins", new[] { "3" })]
    [InlineData("begins batman", new[] { "3" })]
    [InlineData("  ", new[] { "1", "2", "3" })]
    [InlineData("robin", new string[0])]
    public void Search_is_accent_and_case_insensitive_and_multi_term(string query, string[] expected)
    {
        Assert.Equal(expected, Index.Select(new BrowseQuery(Search: query)).Select(m => m.Id));
    }

    [Fact]
    public void Sorts_and_restricts_to_ids()
    {
        Assert.Equal(["2", "3", "1"], Index.Select(new BrowseQuery(Sort: BrowseSort.Title)).Select(m => m.Id));
        Assert.Equal(["2", "1", "3"], Index.Select(new BrowseQuery(Sort: BrowseSort.Rating)).Select(m => m.Id));
        Assert.Equal(["1", "3", "2"], Index.Select(new BrowseQuery(Sort: BrowseSort.RecentlyAdded)).Select(m => m.Id));
        Assert.Equal(["2", "3"], Index.Select(new BrowseQuery(Ids: new HashSet<string> { "3", "2" })).Select(m => m.Id));
        Assert.Equal("Amélie", Index.Item("2")?.Name);
        Assert.Null(Index.Item("nope"));
    }

    [Fact]
    public void Ties_keep_provider_order()
    {
        var index = new BrowseIndex<Movie>([M("a", "Same", null, 5), M("b", "Same", null, 5), M("c", "Same", null, 5)]);

        Assert.Equal(["a", "b", "c"], index.Select(new BrowseQuery(Sort: BrowseSort.Title)).Select(m => m.Id));
        Assert.Equal(["a", "b", "c"], index.Select(new BrowseQuery(Sort: BrowseSort.Rating)).Select(m => m.Id));
    }

    [Fact]
    public void Excluded_categories_are_filtered_out_but_uncategorised_items_stay()
    {
        var index = new BrowseIndex<Movie>([M("1", "Kids show", "kids"), M("2", "Adult show", "adult"), M("3", "No category", null)]);
        var excluded = new HashSet<string> { "adult" };

        Assert.Equal(["1", "3"], index.Select(new BrowseQuery(ExcludedCategories: excluded)).Select(m => m.Id));
        Assert.Empty(index.Select(new BrowseQuery(Search: "adult", ExcludedCategories: excluded)));
    }

    [Fact]
    public void Duplicate_ids_resolve_to_the_first_occurrence()
    {
        var index = new BrowseIndex<Movie>([M("1", "First", null), M("1", "Second", null)]);

        Assert.Equal("First", index.Item("1")?.Name);
    }

    [Theory]
    [InlineData("Straße", "strasse")]
    [InlineData("ＢＡＴＭＡＮ", "batman")]
    [InlineData("ﬁlm", "film")]
    [InlineData("Ærø", "aero")]
    [InlineData("Łódź", "lodz")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("ırmak", "irmak")]
    [InlineData("مُسَلْسَل", "مسلسل")]
    [InlineData("東京", "東京")]
    [InlineData("Ｔｏｋｙｏ　Ｎｅｗｓ", "tokyo news")]
    public void Normalisation_folds_case_accents_width_and_common_letters(string text, string expected)
    {
        Assert.Equal(expected, SearchText.Normalize(text));
    }

    [Fact]
    public void Searching_88k_items_is_fast()
    {
        var names = new[] { "Batman", "Amélie", "News 24", "Le Fabuleux Destin", "東京 Drama", "Strasse" };
        var movies = Enumerable.Range(0, 88_000).Select(i => M(i.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{names[i % names.Length]} {i}", (i % 300).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();

        var build = Stopwatch.StartNew();
        var index = new BrowseIndex<Movie>(movies);
        build.Stop();

        index.Select(new BrowseQuery(Search: "warm up"));
        var search = Stopwatch.StartNew();
        var results = index.Select(new BrowseQuery(Search: "amelie 7", Sort: BrowseSort.Title));
        search.Stop();

        Assert.NotEmpty(results);
        Assert.True(build.Elapsed < TimeSpan.FromSeconds(2), $"Index build took {build.Elapsed}.");
        Assert.True(search.Elapsed < TimeSpan.FromMilliseconds(50), $"Search took {search.Elapsed}.");
    }
}
