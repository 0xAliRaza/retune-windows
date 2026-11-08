using Retune.Core.Playback;

namespace Retune.Core.Models;

/// <summary>User preferences. Unknown or invalid stored values fall back to these defaults.</summary>
public sealed record AppSettings
{
    public static AppSettings Default { get; } = new();

    public Guid? ActivePlaylist { get; init; }

    public LayoutMode LiveLayout { get; init; } = LayoutMode.List;

    public LayoutMode MoviesLayout { get; init; } = LayoutMode.Grid;

    public LayoutMode SeriesLayout { get; init; } = LayoutMode.Grid;

    public TileSize TileSize { get; init; } = TileSize.Medium;

    public LiveFormat LiveFormat { get; init; } = LiveFormat.Automatic;

    public bool AutoplayNext { get; init; } = true;

    /// <summary>0–100, whole numbers.</summary>
    public double Volume { get; init; } = VolumeLevel.Default;

    public SubtitleStyle SubtitleStyle { get; init; } = SubtitleStyle.Default;

    /// <summary>App-wide zoom, 0.75–2 (see <see cref="ZoomLevel"/>).</summary>
    public double Zoom { get; init; } = ZoomLevel.Default;

    /// <summary>The sidebar shows as a slim icon rail (Ctrl+B).</summary>
    public bool SidebarCollapsed { get; init; }

    public LayoutMode LayoutFor(LibrarySection section) => section switch
    {
        LibrarySection.Live => LiveLayout,
        LibrarySection.Movies => MoviesLayout,
        _ => SeriesLayout,
    };

    public AppSettings WithLayout(LibrarySection section, LayoutMode layout) => section switch
    {
        LibrarySection.Live => this with { LiveLayout = layout },
        LibrarySection.Movies => this with { MoviesLayout = layout },
        _ => this with { SeriesLayout = layout },
    };
}
