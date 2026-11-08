namespace Retune.Core.Models;

/// <summary>
/// The main window's last normal (not maximised) bounds in device-independent pixels, and whether it was maximised.
/// Saved on close and restored on launch, so Retune reopens the way it was left.
/// </summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    /// <summary>At least this much of the title bar area must be on a screen for the saved spot to be used.</summary>
    public const double MinVisibleWidth = 120;
    public const double MinVisibleHeight = 40;

    public bool IsValid(double minWidth, double minHeight) =>
        double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height)
        && Width >= minWidth && Height >= minHeight && Width < 100_000 && Height < 100_000;

    /// <summary>
    /// True when enough of the window's top edge lands inside one of the screens (left, top, width, height), so a
    /// window saved on a monitor that has since been unplugged is not restored off-screen.
    /// </summary>
    public bool IsOnScreen(IEnumerable<(double Left, double Top, double Width, double Height)> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        foreach (var s in screens)
        {
            var visibleWidth = Math.Min(Left + Width, s.Left + s.Width) - Math.Max(Left, s.Left);
            var visibleHeight = Math.Min(Top + MinVisibleHeight, s.Top + s.Height) - Math.Max(Top, s.Top);
            if (visibleWidth >= MinVisibleWidth && visibleHeight >= MinVisibleHeight)
            {
                return true;
            }
        }

        return false;
    }
}
