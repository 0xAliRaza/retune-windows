namespace Retune.Core.Models;

/// <summary>
/// The main window's last normal (not maximised) bounds in physical workspace pixels, as Windows reports them, and
/// whether it was maximised. Saved on close and restored on launch, so Retune reopens the way it was left; Windows
/// moves it onto a connected screen if that spot is gone.
/// </summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    public bool IsValid(double minWidth, double minHeight) =>
        double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height)
        && Width >= minWidth && Height >= minHeight && Width < 100_000 && Height < 100_000;
}
