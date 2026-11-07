namespace Retune.Core.Models;

/// <summary>Everything a playlist offers, in provider order. Cached per playlist.</summary>
public sealed record Catalog(
    DateTimeOffset FetchedAt,
    IReadOnlyList<MediaCategory> LiveCategories,
    IReadOnlyList<MediaCategory> VodCategories,
    IReadOnlyList<MediaCategory> SeriesCategories,
    IReadOnlyList<Channel> Channels,
    IReadOnlyList<Movie> Movies,
    IReadOnlyList<Series> Series)
{
    /// <summary>How old a cached catalog may be before a background refresh runs.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);

    public bool IsEmpty => Channels.Count == 0 && Movies.Count == 0 && Series.Count == 0;

    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) => now - FetchedAt > maxAge;

    public IReadOnlyList<MediaCategory> CategoriesFor(LibrarySection section) => section switch
    {
        LibrarySection.Live => LiveCategories,
        LibrarySection.Movies => VodCategories,
        LibrarySection.Series => SeriesCategories,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    public int CountFor(LibrarySection section) => section switch
    {
        LibrarySection.Live => Channels.Count,
        LibrarySection.Movies => Movies.Count,
        LibrarySection.Series => Series.Count,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };
}
