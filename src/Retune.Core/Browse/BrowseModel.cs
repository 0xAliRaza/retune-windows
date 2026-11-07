using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Search;

namespace Retune.Core.Browse;

/// <summary>Everything browse decisions depend on, for one loaded playlist.</summary>
/// <param name="UnlockedCategories">Category keys unlocked with the PIN this session.</param>
public sealed record BrowseContext(Guid PlaylistId, CatalogIndex Index, LibraryState Library, IReadOnlySet<string> UnlockedCategories)
{
    public Catalog Catalog => Index.Catalog;
}

/// <summary>What the content column shows.</summary>
public abstract record BrowseContent;

/// <summary>A locked category is selected: show the unlock prompt instead of its items.</summary>
public sealed record LockedContent(string CategoryId) : BrowseContent;

public sealed record ItemsContent(IReadOnlyList<IBrowsable> Items) : BrowseContent;

public sealed record ContinueWatchingContent(IReadOnlyList<KeyedProgress> Entries) : BrowseContent;

/// <summary>A hidden category as listed in Settings (ids overlap across sections, so the section is kept).</summary>
public sealed record HiddenCategory(LibrarySection Section, string Id, string Name);

/// <summary>
/// The browse rules: exclusions, category ordering, default filter, titles and content.
/// Pure functions over <see cref="BrowseContext"/>. Spec: docs/spec/core.md §4.3–4.5.
/// </summary>
public static class BrowseModel
{
    public static bool IsSearching(string? search) => !string.IsNullOrWhiteSpace(search);

    public static string CategoryKey(BrowseContext context, LibrarySection section, string categoryId) =>
        LibraryKeys.Category(context.PlaylistId, section, categoryId);

    public static bool IsLocked(BrowseContext context, LibrarySection section, string categoryId) =>
        context.Library.LockedCategories.Contains(CategoryKey(context, section, categoryId));

    public static bool IsUnlocked(BrowseContext context, LibrarySection section, string categoryId) =>
        context.UnlockedCategories.Contains(CategoryKey(context, section, categoryId));

    /// <summary>Locked and not unlocked this session.</summary>
    public static bool IsBlocked(BrowseContext context, LibrarySection section, string categoryId) =>
        IsLocked(context, section, categoryId) && !IsUnlocked(context, section, categoryId);

    public static bool IsHidden(BrowseContext context, LibrarySection section, string categoryId) =>
        context.Library.HiddenCategories.Contains(CategoryKey(context, section, categoryId));

    /// <summary>Hidden categories plus locked ones that aren't unlocked this session.</summary>
    public static IReadOnlySet<string> ExcludedCategories(BrowseContext context, LibrarySection section)
    {
        var excluded = LibraryKeys.CategoryIds(context.Library.HiddenCategories, context.PlaylistId, section).ToHashSet(StringComparer.Ordinal);
        foreach (var id in LibraryKeys.CategoryIds(context.Library.LockedCategories, context.PlaylistId, section))
        {
            if (!IsUnlocked(context, section, id))
            {
                excluded.Add(id);
            }
        }

        return excluded;
    }

    /// <summary>Provider categories minus hidden ones, split into pinned (in pin order) and the rest (provider order).</summary>
    public static (IReadOnlyList<MediaCategory> Pinned, IReadOnlyList<MediaCategory> Others) BrowsableCategories(BrowseContext context, LibrarySection section)
    {
        var visible = context.Catalog.CategoriesFor(section).Where(c => !IsHidden(context, section, c.Id)).ToList();
        var byId = new Dictionary<string, MediaCategory>(StringComparer.Ordinal);
        foreach (var category in visible)
        {
            byId.TryAdd(category.Id, category);
        }

        var pinnedIds = LibraryKeys.CategoryIds(context.Library.PinnedCategories, context.PlaylistId, section).ToList();
        var pinned = pinnedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var pinnedSet = pinnedIds.ToHashSet(StringComparer.Ordinal);
        return (pinned, visible.Where(c => !pinnedSet.Contains(c.Id)).ToList());
    }

    public static IReadOnlyList<HiddenCategory> HiddenCategories(BrowseContext context)
    {
        var result = new List<HiddenCategory>();
        foreach (var section in Enum.GetValues<LibrarySection>())
        {
            var names = context.Catalog.CategoriesFor(section).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
            result.AddRange(LibraryKeys.CategoryIds(context.Library.HiddenCategories, context.PlaylistId, section)
                .Select(id => new HiddenCategory(section, id, names.TryGetValue(id, out var name) ? name.Trim() : $"Category {id}")));
        }

        return result;
    }

    /// <summary>Continue Watching for one section, minus entries whose (known) category is excluded.</summary>
    public static IReadOnlyList<KeyedProgress> ContinueWatching(BrowseContext context, LibrarySection section)
    {
        if (section == LibrarySection.Live)
        {
            return [];
        }

        var excluded = ExcludedCategories(context, section);
        return context.Library.ContinueWatching(context.PlaylistId)
            .Where(entry => section == LibrarySection.Movies
                ? entry.Progress.Item.Kind == ContentKind.Movie
                    && !IsExcluded(context.Index.Movies.Item(entry.Progress.Item.Id)?.CategoryId, excluded)
                : entry.Progress.Item.Kind == ContentKind.Episode
                    && !IsExcluded(entry.Progress.Item.ParentId is { } parent ? context.Index.Series.Item(parent)?.CategoryId : null, excluded))
            .ToList();
    }

    /// <summary>
    /// Live: All. Movies/Series: Continue Watching when it has entries, otherwise the first browsable
    /// category that isn't locked (scanning all of them is slow), otherwise All.
    /// </summary>
    public static CategoryFilter DefaultFilter(BrowseContext context, LibrarySection section)
    {
        if (section == LibrarySection.Live)
        {
            return CategoryFilter.All;
        }

        if (ContinueWatching(context, section).Count > 0)
        {
            return CategoryFilter.ContinueWatching;
        }

        var (pinned, others) = BrowsableCategories(context, section);
        var first = pinned.Concat(others).FirstOrDefault(c => !IsLocked(context, section, c.Id));
        return first is null ? CategoryFilter.All : CategoryFilter.Category(first.Id);
    }

    public static string Title(BrowseContext context, LibrarySection section, CategoryFilter filter, string? search) => (filter, IsSearching(search)) switch
    {
        (_, true) => $"Results for “{search!.Trim()}”",
        (AllFilter, _) => $"All {section.Title()}",
        (FavoritesFilter, _) => "Favorites",
        (ContinueWatchingFilter, _) => "Continue Watching",
        (RecentFilter, _) => "Recently Watched",
        (CategoryIdFilter c, _) => context.Catalog.CategoriesFor(section).FirstOrDefault(x => x.Id == c.Id)?.Name.Trim() is { Length: > 0 } name ? name : section.Title(),
        _ => section.Title(),
    };

    public static BrowseContent Content(BrowseContext context, LibrarySection section, CategoryFilter filter, string? search, BrowseSort sort)
    {
        var searching = IsSearching(search);
        if (!searching && filter is CategoryIdFilter { Id: var lockedId } && IsBlocked(context, section, lockedId))
        {
            return new LockedContent(lockedId);
        }

        if (!searching && filter is ContinueWatchingFilter)
        {
            return new ContinueWatchingContent(ContinueWatching(context, section));
        }

        var excluded = ExcludedCategories(context, section);
        if (!searching && section == LibrarySection.Live && filter is RecentFilter)
        {
            var recent = context.Library.RecentChannelIds(context.PlaylistId)
                .Select(context.Index.Channels.Item)
                .OfType<Channel>()
                .Where(c => !IsExcluded(c.CategoryId, excluded))
                .ToList<IBrowsable>();
            return new ItemsContent(recent);
        }

        var category = !searching && filter is CategoryIdFilter c ? c.Id : null;
        var ids = !searching && filter is FavoritesFilter ? context.Library.FavoriteIds(context.PlaylistId, section.ToContentKind()) : null;
        var query = new BrowseQuery(category, searching ? search : null, ids, category is null ? excluded : null, sort);
        return new ItemsContent(section switch
        {
            LibrarySection.Live => context.Index.Channels.Select(query),
            LibrarySection.Movies => context.Index.Movies.Select(query),
            _ => context.Index.Series.Select(query),
        });
    }

    private static bool IsExcluded(string? categoryId, IReadOnlySet<string> excluded) =>
        categoryId is not null && excluded.Contains(categoryId);
}
