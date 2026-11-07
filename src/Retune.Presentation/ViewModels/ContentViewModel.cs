using CommunityToolkit.Mvvm.ComponentModel;
using Retune.Core.Browse;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Navigation;

namespace Retune.Presentation.ViewModels;

public enum ContentState
{
    NoPlaylist,
    Loading,
    Failed,
    Locked,
    Empty,
    Items,
}

/// <summary>The content column: a virtualised list, grid rows, or one of the empty/loading/error/locked states.</summary>
public sealed partial class ContentViewModel : ObservableObject
{
    public const double GridSpacing = 20;
    public const double GridPadding = 24;

    /// <summary>Boxed layout values for XAML command parameters.</summary>
    public static readonly object GridLayout = LayoutMode.Grid;
    public static readonly object ListLayout = LayoutMode.List;
    public static readonly object CompactLayout = LayoutMode.Compact;

    private Guid _playlistId;
    private LibraryState _library = LibraryState.Empty;
    private string? _playingId;
    private List<object> _entries = [];

    [ObservableProperty]
    public partial ContentState State { get; private set; } = ContentState.NoPlaylist;

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial LazyList<BrowseItemViewModel> Items { get; private set; } = LazyList.Empty<BrowseItemViewModel>();

    [ObservableProperty]
    public partial LazyList<TileRow> Rows { get; private set; } = LazyList.Empty<TileRow>();

    [ObservableProperty]
    public partial LayoutMode Layout { get; set; } = LayoutMode.Grid;

    [ObservableProperty]
    public partial TileSize TileSize { get; set; } = TileSize.Medium;

    /// <summary>Grid columns that fit the current width (set by the view).</summary>
    [ObservableProperty]
    public partial int Columns { get; private set; } = 1;

    [ObservableProperty]
    public partial string StatusTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    [ObservableProperty]
    public partial string? LockedCategoryId { get; private set; }

    /// <summary>Continue Watching shows landscape cards in the grid.</summary>
    public bool IsLandscape => _entries.Count > 0 && _entries[0] is KeyedProgress;

    public double MinTileWidth => TileSize.MinColumnWidth() * (IsLandscape ? 1.7 : 1);

    public void ShowStatus(ContentState state, string title, string? message = null)
    {
        _entries = [];
        Items = LazyList.Empty<BrowseItemViewModel>();
        Rows = LazyList.Empty<TileRow>();
        StatusTitle = title;
        StatusMessage = message;
        State = state;
    }

    public void Show(Guid playlistId, string title, BrowseContent content, LibraryState library, string? playingId, (string Title, string? Message) emptyText)
    {
        ArgumentNullException.ThrowIfNull(content);
        _playlistId = playlistId;
        _library = library;
        _playingId = playingId;
        Title = title;
        LockedCategoryId = null;

        switch (content)
        {
            case LockedContent locked:
                _entries = [];
                LockedCategoryId = locked.CategoryId;
                Items = LazyList.Empty<BrowseItemViewModel>();
                Rows = LazyList.Empty<TileRow>();
                State = ContentState.Locked;
                return;
            case ContinueWatchingContent cw:
                _entries = cw.Entries.Cast<object>().ToList();
                break;
            case ItemsContent items:
                _entries = items.Items.Cast<object>().ToList();
                break;
            default:
                _entries = [];
                break;
        }

        if (_entries.Count == 0)
        {
            ShowStatus(ContentState.Empty, emptyText.Title, emptyText.Message);
            return;
        }

        Items = new LazyList<BrowseItemViewModel>(_entries.Count, Create);
        RebuildRows();
        OnPropertyChanged(nameof(IsLandscape));
        OnPropertyChanged(nameof(MinTileWidth));
        State = ContentState.Items;
    }

    /// <summary>Library or now-playing changed: update the built tiles in place (keeps scroll position).</summary>
    public void Refresh(LibraryState library, string? playingId)
    {
        _library = library;
        _playingId = playingId;
        foreach (var item in Items.Realized)
        {
            item.Refresh(library, playingId);
        }
    }

    /// <summary>The view reports its width; the column count follows the spec formula.</summary>
    public void SetAvailableWidth(double width)
    {
        var columns = GridNavigation.Columns(width - (2 * GridPadding), MinTileWidth, GridSpacing);
        if (columns != Columns)
        {
            Columns = columns;
            RebuildRows();
        }
    }

    /// <summary>The entry behind an item, for building lineups from what's on screen.</summary>
    public IReadOnlyList<object> Entries => _entries;

    partial void OnTileSizeChanged(TileSize value) => OnPropertyChanged(nameof(MinTileWidth));

    private BrowseItemViewModel Create(int index) => _entries[index] switch
    {
        KeyedProgress entry => new BrowseItemViewModel(_playlistId, entry, _library, _playingId),
        IBrowsable item => new BrowseItemViewModel(_playlistId, item, _library, _playingId),
        _ => throw new InvalidOperationException("Unknown browse entry."),
    };

    private void RebuildRows()
    {
        var columns = Math.Max(1, Columns);
        var count = Items.Count;
        var items = Items;
        Rows = new LazyList<TileRow>((count + columns - 1) / columns, row =>
        {
            var first = row * columns;
            var tiles = new List<BrowseItemViewModel>(columns);
            for (var i = first; i < Math.Min(first + columns, count); i++)
            {
                tiles.Add(items[i]);
            }

            return new TileRow(tiles, first);
        });
    }
}
