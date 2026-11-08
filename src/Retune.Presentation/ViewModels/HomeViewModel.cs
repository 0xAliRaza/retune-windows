using CommunityToolkit.Mvvm.ComponentModel;
using Retune.Core.Browse;
using Retune.Core.Library;
using Retune.Core.Models;

namespace Retune.Presentation.ViewModels;

/// <summary>A row on the Home page: its tiles, the entries they play from, and where "See all" goes.</summary>
public sealed record HomeRow(HomeRowContent Content, IReadOnlyList<BrowseItemViewModel> Items)
{
    public string Title => Content.Title;

    /// <summary>Continue Watching shows wide stills; channels show logos; films and series show posters.</summary>
    public bool IsLandscape => Content.Kind == HomeRowKind.ContinueWatching;

    public bool HasMore => Content.Total > Items.Count;

    public string SeeAllLabel => $"See all {Content.Total.ToString(System.Globalization.CultureInfo.CurrentCulture)}";
}

/// <summary>Selection state for the sidebar's Home row (the nav row style reads IsSelected).</summary>
public sealed record NavState(bool IsSelected);

/// <summary>
/// The Home page (the landing page): Continue Watching, recently watched channels and favourites, from
/// <see cref="BrowseModel.HomeRows"/>. UI thread only.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    /// <summary>Tiles per row; the rest is one click away under "See all".</summary>
    public const int MaxPerRow = 24;

    [ObservableProperty]
    public partial IReadOnlyList<HomeRow> Rows { get; private set; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public void Show(BrowseContext? context, string? playingId)
    {
        Rows = context is null
            ? []
            : [.. BrowseModel.HomeRows(context, MaxPerRow).Select(row => new HomeRow(row, [.. row.Entries.Select(e => Tile(context, e, playingId))]))];
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static BrowseItemViewModel Tile(BrowseContext context, object entry, string? playingId) => entry switch
    {
        KeyedProgress progress => new BrowseItemViewModel(context.PlaylistId, progress, context.Library, playingId),
        IBrowsable item => new BrowseItemViewModel(context.PlaylistId, item, context.Library, playingId),
        _ => throw new ArgumentException("Unknown Home entry.", nameof(entry)),
    };
}
