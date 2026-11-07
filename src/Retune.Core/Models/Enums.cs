namespace Retune.Core.Models;

/// <summary>What a piece of content is. The key strings are persisted and embedded in library keys.</summary>
public enum ContentKind
{
    Live,
    Movie,
    Series,
    Episode,
}

/// <summary>The three browse sections of the app.</summary>
public enum LibrarySection
{
    Live,
    Movies,
    Series,
}

public enum LayoutMode
{
    Grid,
    List,
    Compact,
}

public enum TileSize
{
    Small,
    Medium,
    Large,
}

public enum BrowseSort
{
    /// <summary>Provider order ("Default").</summary>
    Provider,
    Title,
    RecentlyAdded,
    Rating,
}

/// <summary>Live stream container preference for Xtream live URLs.</summary>
public enum LiveFormat
{
    Automatic,
    Ts,
    Hls,
}

public static class EnumExtensions
{
    /// <summary>The stable string used in keys and persisted data.</summary>
    public static string ToKey(this ContentKind kind) => kind switch
    {
        ContentKind.Live => "live",
        ContentKind.Movie => "movie",
        ContentKind.Series => "series",
        ContentKind.Episode => "episode",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ContentKind? ParseContentKind(string? key) => key switch
    {
        "live" => ContentKind.Live,
        "movie" => ContentKind.Movie,
        "series" => ContentKind.Series,
        "episode" => ContentKind.Episode,
        _ => null,
    };

    public static ContentKind ToContentKind(this LibrarySection section) => section switch
    {
        LibrarySection.Live => ContentKind.Live,
        LibrarySection.Movies => ContentKind.Movie,
        LibrarySection.Series => ContentKind.Series,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    public static string Title(this LibrarySection section) => section switch
    {
        LibrarySection.Live => "Live TV",
        LibrarySection.Movies => "Movies",
        LibrarySection.Series => "Series",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    /// <summary>The grid's minimum column width in device-independent pixels.</summary>
    public static double MinColumnWidth(this TileSize size) => size switch
    {
        TileSize.Small => 112,
        TileSize.Medium => 150,
        TileSize.Large => 210,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    public static string Title(this BrowseSort sort) => sort switch
    {
        BrowseSort.Provider => "Default",
        BrowseSort.Title => "Title",
        BrowseSort.RecentlyAdded => "Recently Added",
        BrowseSort.Rating => "Rating",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
    };

    public static string Title(this LiveFormat format) => format switch
    {
        LiveFormat.Automatic => "Automatic",
        LiveFormat.Ts => "MPEG-TS (lower latency)",
        LiveFormat.Hls => "HLS (more resilient)",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
