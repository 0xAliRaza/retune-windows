using CommunityToolkit.Mvvm.ComponentModel;
using Retune.Core.Browse;
using Retune.Core.Models;
using Retune.Core.Search;

namespace Retune.Presentation.ViewModels;

public enum CategoryRowKind
{
    All,
    Favorites,
    ContinueWatching,
    Recent,
    Category,
}

/// <summary>A row in the category column.</summary>
public sealed record CategoryRow(
    CategoryRowKind Kind,
    string Title,
    string? CategoryId,
    int? Count,
    bool IsLocked,
    bool IsUnlocked,
    bool IsPinned,
    bool IsSelected = false)
{
    public CategoryFilter Filter => Kind switch
    {
        CategoryRowKind.All => CategoryFilter.All,
        CategoryRowKind.Favorites => CategoryFilter.Favorites,
        CategoryRowKind.ContinueWatching => CategoryFilter.ContinueWatching,
        CategoryRowKind.Recent => CategoryFilter.Recent,
        _ => CategoryFilter.Category(CategoryId!),
    };

    /// <summary>Locked categories hide their count so it can't reveal what they hold.</summary>
    public string? CountText => IsLocked && !IsUnlocked ? null : Count is > 0 ? Count.Value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : null;
}

/// <summary>The category column: fixed filters, pinned categories, then the rest; filterable.</summary>
public sealed partial class CategoryListViewModel : ObservableObject
{
    private BrowseContext? _context;
    private LibrarySection _section;
    private CategoryFilter _selected = CategoryFilter.All;

    [ObservableProperty]
    public partial IReadOnlyList<CategoryRow> Fixed { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CategoryRow> Pinned { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CategoryRow> Categories { get; private set; } = [];

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SectionTitle { get; private set; } = string.Empty;

    public void Show(BrowseContext? context, LibrarySection section, CategoryFilter selected)
    {
        _selected = selected;
        if (_section != section)
        {
            FilterText = string.Empty;
        }

        _context = context;
        _section = section;
        SectionTitle = section.Title();
        Rebuild();
    }

    partial void OnFilterTextChanged(string value) => Rebuild();

    private void Rebuild()
    {
        if (_context is not { } context)
        {
            Fixed = Pinned = Categories = [];
            return;
        }

        var index = context.Index;
        var total = context.Catalog.CountFor(_section);
        Fixed =
        [
            Mark(new(CategoryRowKind.All, "All", null, total, false, false, false)),
            Mark(new(CategoryRowKind.Favorites, "Favorites", null, null, false, false, false)),
            Mark(_section == LibrarySection.Live
                ? new(CategoryRowKind.Recent, "Recently Watched", null, null, false, false, false)
                : new(CategoryRowKind.ContinueWatching, "Continue Watching", null, null, false, false, false)),
        ];

        var terms = SearchText.Terms(FilterText);
        CategoryRow Row(MediaCategory c, bool pinned) => Mark(new(
            CategoryRowKind.Category,
            c.Name.Trim(),
            c.Id,
            _section switch
            {
                LibrarySection.Live => index.Channels.Count(c.Id),
                LibrarySection.Movies => index.Movies.Count(c.Id),
                _ => index.Series.Count(c.Id),
            },
            BrowseModel.IsLocked(context, _section, c.Id),
            BrowseModel.IsUnlocked(context, _section, c.Id),
            pinned));
        bool Visible(MediaCategory c) => terms.Length == 0 || SearchText.Matches(SearchText.Normalize(c.Name), terms);

        var (pinned, others) = BrowseModel.BrowsableCategories(context, _section);
        Pinned = pinned.Where(Visible).Select(c => Row(c, true)).ToList();
        Categories = others.Where(Visible).Select(c => Row(c, false)).ToList();
    }

    private CategoryRow Mark(CategoryRow row) => row.Filter == _selected ? row with { IsSelected = true } : row;
}
