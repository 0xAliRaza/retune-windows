using System.Globalization;

namespace Retune.Core.Models;

/// <summary>A provider category. Ids can repeat across sections, so always pair one with its section.</summary>
public sealed record MediaCategory(string Id, string Name);

/// <summary>Anything that appears in a browse list.</summary>
public interface IBrowsable
{
    string Id { get; }

    string Name { get; }

    string? CategoryId { get; }

    /// <summary>Date used by "Recently Added" sorting, if any.</summary>
    DateTimeOffset? SortDate { get; }

    /// <summary>0–10 rating used by "Rating" sorting, if any.</summary>
    double? Rating { get; }
}

public sealed record Channel(
    string Id,
    int? Number,
    string Name,
    Uri? Logo,
    string? CategoryId,
    string? EpgChannelId,
    string? DirectUrl,
    bool HasArchive) : IBrowsable
{
    public DateTimeOffset? SortDate => null;

    public double? Rating => null;
}

public sealed record Movie(
    string Id,
    string Name,
    Uri? Poster,
    string? CategoryId,
    double? Rating,
    DateTimeOffset? Added,
    string? ContainerExtension,
    string? DirectUrl) : IBrowsable
{
    public DateTimeOffset? SortDate => Added;
}

public sealed record Series(
    string Id,
    string Name,
    Uri? Cover,
    Uri? Backdrop,
    string? CategoryId,
    string? Plot,
    string? Genre,
    string? ReleaseDate,
    double? Rating,
    DateTimeOffset? LastModified) : IBrowsable
{
    public DateTimeOffset? SortDate => LastModified;

    public string? Year => MediaInfo.YearOf(ReleaseDate);
}

/// <summary>Detail metadata for a movie or series. Every field is optional.</summary>
public sealed record MediaInfo(
    string? Plot = null,
    string? Cast = null,
    string? Director = null,
    string? Genre = null,
    string? ReleaseDate = null,
    int? DurationSeconds = null,
    double? Rating = null,
    Uri? Poster = null,
    Uri? Backdrop = null,
    string? YoutubeTrailer = null)
{
    public static MediaInfo Empty { get; } = new();

    public string? Year => YearOf(ReleaseDate);

    /// <summary>The first four characters, if there are at least four and they form an integer.</summary>
    internal static string? YearOf(string? releaseDate) =>
        releaseDate is { Length: >= 4 } && int.TryParse(releaseDate.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? releaseDate[..4]
            : null;
}

public sealed record Episode(
    string Id,
    int Season,
    int Number,
    string Title,
    string? ContainerExtension = null,
    string? Plot = null,
    Uri? Image = null,
    int? DurationSeconds = null,
    string? DirectUrl = null);

public sealed record Season(int Number, string Name, Uri? Cover, IReadOnlyList<Episode> Episodes);

public sealed record SeriesDetail(MediaInfo Info, IReadOnlyList<Season> Seasons)
{
    /// <summary>Every episode in play order: seasons ascending, then episode numbers ascending.</summary>
    public IEnumerable<Episode> EpisodesInOrder => Seasons.SelectMany(s => s.Episodes);
}
