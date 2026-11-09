namespace Retune.Presentation.Player;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>
/// Rules for the mini player: when switching apps opens it, and where it goes. The window code only applies them.
/// </summary>
public static class PictureInPicture
{
    /// <summary>Width of the mini window in device-independent pixels (16:9, so 270 high).</summary>
    public const double Width = 480;

    /// <summary>Gap to the work area's edges (the taskbar stays visible), in device-independent pixels.</summary>
    public const double Margin = 16;

    /// <summary>The mini window never takes more than this share of the screen's width.</summary>
    public const double MaxScreenShare = 0.5;

    private const double AspectRatio = 16.0 / 9.0;

    /// <summary>
    /// Switching away opens the mini window only while something is actually playing, the setting is on and the user
    /// didn't minimise Retune (a deliberate "out of the way").
    /// </summary>
    public static bool OpensOnSwitchAway(bool enabled, bool presented, bool paused, bool minimized, bool alreadyOpen) =>
        enabled && presented && !paused && !minimized && !alreadyOpen;

    /// <summary>The bottom-right corner of the work area, scaled for its DPI.</summary>
    public static PixelBounds Place(PixelBounds workArea, double dpiScale)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0 || !double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "The work area and DPI scale must be positive.");
        }

        var margin = (int)Math.Round(Margin * dpiScale);
        var width = Math.Min(Width * dpiScale, workArea.Width * MaxScreenShare);
        width = Math.Min(width, (workArea.Height - (2 * margin)) * AspectRatio);
        var w = Math.Max(1, (int)Math.Round(width));
        var h = Math.Max(1, (int)Math.Round(width / AspectRatio));
        return new PixelBounds(workArea.Right - margin - w, workArea.Bottom - margin - h, w, h);
    }
}
