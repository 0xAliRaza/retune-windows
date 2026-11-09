using System.Windows;
using System.Windows.Interop;
using Retune.App.Interop;

namespace Retune.App.Windowing;

/// <summary>
/// Moves a window by dragging anywhere on it (the mini player has no title bar). Done by hand rather than with
/// Windows' own move loop, which would activate the window, and so that a press without movement stays a click.
/// </summary>
public sealed class WindowDrag
{
    private readonly nint _hwnd;
    private readonly NativeMethods.CursorPoint _start;
    private readonly NativeMethods.Rect _origin;
    private readonly int _threshold;

    private WindowDrag(nint hwnd, NativeMethods.CursorPoint start, NativeMethods.Rect origin, int threshold)
    {
        _hwnd = hwnd;
        _start = start;
        _origin = origin;
        _threshold = threshold;
    }

    /// <summary>The pointer went past the drag distance, so the press was a move, not a click.</summary>
    public bool Moved { get; private set; }

    public static WindowDrag? Begin(Window window, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || !NativeMethods.GetCursorPos(out var cursor) || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        var threshold = (int)Math.Ceiling(Math.Max(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance) * dpiScale);
        return new WindowDrag(hwnd, cursor, rect, threshold);
    }

    /// <summary>Call on every mouse move while the button is down.</summary>
    public void Follow()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var dx = cursor.X - _start.X;
        var dy = cursor.Y - _start.Y;
        if (!Moved && Math.Abs(dx) < _threshold && Math.Abs(dy) < _threshold)
        {
            return;
        }

        Moved = true;
        NativeMethods.SetWindowPos(_hwnd, 0, _origin.Left + dx, _origin.Top + dy, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }
}
