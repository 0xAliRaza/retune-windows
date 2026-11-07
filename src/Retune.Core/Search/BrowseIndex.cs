using Retune.Core.Models;

namespace Retune.Core.Search;

/// <summary>What to show from one section's list.</summary>
/// <param name="CategoryId">Only this category, or null for all.</param>
/// <param name="Ids">Only these ids (favourites, recents), or null.</param>
/// <param name="ExcludedCategories">Hidden or still-locked categories. Items without a category are never excluded.</param>
public sealed record BrowseQuery(
    string? CategoryId = null,
    string? Search = null,
    IReadOnlySet<string>? Ids = null,
    IReadOnlySet<string>? ExcludedCategories = null,
    BrowseSort Sort = BrowseSort.Provider);

/// <summary>
/// A precomputed index over one catalog list so browsing 88k+ items stays instant:
/// names are normalised once, categories map to offsets, ids look up in O(1).
/// Immutable and safe to read from any thread.
/// </summary>
public sealed class BrowseIndex<T>
    where T : class, IBrowsable
{
    private readonly string[] _normalizedNames;
    private readonly Dictionary<string, int[]> _byCategory;
    private readonly Dictionary<string, int> _positionById;

    public BrowseIndex(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
        _normalizedNames = new string[items.Count];
        var byCategory = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        _positionById = new Dictionary<string, int>(items.Count, StringComparer.Ordinal);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            _normalizedNames[i] = SearchText.Normalize(item.Name);
            _positionById.TryAdd(item.Id, i); // first occurrence wins
            if (item.CategoryId is { } category)
            {
                if (!byCategory.TryGetValue(category, out var offsets))
                {
                    byCategory[category] = offsets = [];
                }

                offsets.Add(i);
            }
        }

        _byCategory = byCategory.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>Everything, in provider order.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Items in a category (exclusions don't apply to counts).</summary>
    public int Count(string categoryId) => _byCategory.TryGetValue(categoryId, out var offsets) ? offsets.Length : 0;

    public T? Item(string id) => _positionById.TryGetValue(id, out var i) ? Items[i] : null;

    public IReadOnlyList<T> Select(BrowseQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var terms = SearchText.Terms(query.Search);
        var excluded = query.ExcludedCategories is { Count: > 0 } set ? set : null;
        var matches = new List<int>();

        foreach (var i in Offsets(query.CategoryId))
        {
            var item = Items[i];
            if (query.Ids is { } ids && !ids.Contains(item.Id))
            {
                continue;
            }

            if (excluded is not null && item.CategoryId is { } category && excluded.Contains(category))
            {
                continue;
            }

            if (terms.Length > 0 && !SearchText.Matches(_normalizedNames[i], terms))
            {
                continue;
            }

            matches.Add(i);
        }

        // LINQ ordering is stable, so ties keep provider order.
        IEnumerable<int> ordered = query.Sort switch
        {
            BrowseSort.Title => matches.OrderBy(i => _normalizedNames[i], StringComparer.Ordinal),
            BrowseSort.RecentlyAdded => matches.OrderByDescending(i => Items[i].SortDate ?? DateTimeOffset.MinValue),
            BrowseSort.Rating => matches.OrderByDescending(i => Items[i].Rating ?? -1),
            _ => matches,
        };

        return ordered.Select(i => Items[i]).ToList();
    }

    private IEnumerable<int> Offsets(string? categoryId) =>
        categoryId is null
            ? Enumerable.Range(0, Items.Count)
            : _byCategory.TryGetValue(categoryId, out var offsets) ? offsets : [];
}

/// <summary>The three indexes of a loaded catalog, built in parallel off the UI thread.</summary>
public sealed class CatalogIndex
{
    private CatalogIndex(Catalog catalog, BrowseIndex<Channel> channels, BrowseIndex<Movie> movies, BrowseIndex<Series> series)
    {
        Catalog = catalog;
        Channels = channels;
        Movies = movies;
        Series = series;
    }

    public Catalog Catalog { get; }

    public BrowseIndex<Channel> Channels { get; }

    public BrowseIndex<Movie> Movies { get; }

    public BrowseIndex<Series> Series { get; }

    public static async Task<CatalogIndex> BuildAsync(Catalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var channels = Task.Run(() => new BrowseIndex<Channel>(catalog.Channels), cancellationToken);
        var movies = Task.Run(() => new BrowseIndex<Movie>(catalog.Movies), cancellationToken);
        var series = Task.Run(() => new BrowseIndex<Series>(catalog.Series), cancellationToken);
        await Task.WhenAll(channels, movies, series).ConfigureAwait(false);
        return new CatalogIndex(catalog, await channels.ConfigureAwait(false), await movies.ConfigureAwait(false), await series.ConfigureAwait(false));
    }

    /// <summary>Synchronous build for tests and small catalogs.</summary>
    public static CatalogIndex Build(Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new CatalogIndex(catalog, new(catalog.Channels), new(catalog.Movies), new(catalog.Series));
    }
}
