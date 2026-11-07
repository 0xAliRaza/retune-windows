using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Retune.Core.Formatting;
using Retune.Core.Library;
using Retune.Core.Models;

namespace Retune.Presentation.ViewModels;

public enum ArtworkStyle
{
    /// <summary>2:3 movie and series posters.</summary>
    Poster,

    /// <summary>Channel logos, fitted on a neutral tile.</summary>
    Logo,

    /// <summary>16:9 Continue Watching cards.</summary>
    Landscape,
}

/// <summary>One tile or row in a browse list. Library-dependent flags refresh in place via <see cref="Refresh"/>.</summary>
public sealed partial class BrowseItemViewModel : ObservableObject
{
    private readonly Guid _playlistId;

    public BrowseItemViewModel(Guid playlistId, IBrowsable source, LibraryState library, string? playingId)
    {
        ArgumentNullException.ThrowIfNull(source);
        _playlistId = playlistId;
        Source = source;
        (Kind, Style) = source switch
        {
            Channel => (ContentKind.Live, ArtworkStyle.Logo),
            Movie => (ContentKind.Movie, ArtworkStyle.Poster),
            _ => (ContentKind.Series, ArtworkStyle.Poster),
        };
        Title = source.Name.Trim();
        Initials = ArtworkPlaceholder.Initials(Title);
        (Number, Artwork, Subtitle) = source switch
        {
            Channel c => (c.Number?.ToString(CultureInfo.InvariantCulture), c.Logo, (string?)null),
            Movie m => (null, m.Poster, m.Added is { } added ? $"Added {added.ToLocalTime():d MMM yyyy}" : null),
            Series s => (null, s.Cover, string.Join(" · ", new[] { s.Year, s.Genre }.Where(p => !string.IsNullOrWhiteSpace(p)))),
            _ => (null, null, null),
        };
        RatingText = source.Rating is { } r ? Format.Rating(r) : null;
        Refresh(library, playingId);
    }

    /// <summary>A Continue Watching card.</summary>
    public BrowseItemViewModel(Guid playlistId, KeyedProgress entry, LibraryState library, string? playingId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _playlistId = playlistId;
        Entry = entry;
        var item = entry.Progress.Item;
        Kind = item.Kind;
        Style = ArtworkStyle.Landscape;
        Title = item.Title;
        Initials = ArtworkPlaceholder.Initials(Title);
        Artwork = item.Artwork;
        Subtitle = string.Join(" · ", new[] { item.Subtitle, Format.Remaining(entry.Progress.Remaining) }.Where(p => !string.IsNullOrWhiteSpace(p)));
        Refresh(library, playingId);
    }

    /// <summary>The catalog entry, or null for a Continue Watching card.</summary>
    public IBrowsable? Source { get; }

    /// <summary>The resume point, for Continue Watching cards.</summary>
    public KeyedProgress? Entry { get; }

    public ContentKind Kind { get; }

    public ArtworkStyle Style { get; }

    public string Title { get; }

    public string? Subtitle { get; }

    public string? Number { get; }

    public Uri? Artwork { get; }

    public string Initials { get; }

    public string? RatingText { get; }

    /// <summary>Grid tiles caption series with year and genre; movies show none (their date is list-only).</summary>
    public string? TileCaption => Kind == ContentKind.Movie ? null : Subtitle;

    public string Id => Source?.Id ?? Entry!.Progress.Item.Id;

    public string LibraryKey => Entry?.Key ?? LibraryKeys.Content(_playlistId, Kind, Id);

    [ObservableProperty]
    public partial bool IsFavorite { get; private set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    public partial bool IsWatched { get; private set; }

    /// <summary>0–1, or 0 when there is no meaningful progress (under 1%).</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    public bool HasProgress => Progress > 0;

    public bool CanFavorite => Entry is null;

    public void Refresh(LibraryState library, string? playingId)
    {
        ArgumentNullException.ThrowIfNull(library);
        IsFavorite = Entry is null && library.IsFavorite(LibraryKey);
        IsPlaying = playingId is not null && playingId == Id;
        var progress = Entry?.Progress ?? (Kind == ContentKind.Movie ? library.ProgressFor(LibraryKey) : null);
        IsWatched = progress?.IsFinished == true;
        Progress = progress is { Fraction: > 0.01 } p ? p.Fraction : 0;
        OnPropertyChanged(nameof(HasProgress));
    }
}

/// <summary>One row of a virtualised poster grid.</summary>
public sealed record TileRow(IReadOnlyList<BrowseItemViewModel> Items, int FirstIndex);
