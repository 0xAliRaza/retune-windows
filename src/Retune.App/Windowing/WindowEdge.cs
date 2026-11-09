using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Retune.App.Interop;

namespace Retune.App.Windowing;

/// <summary>How Windows 11 draws the window's outline.</summary>
public enum EdgeStyle
{
    /// <summary>The system's 1-pixel border and round corners.</summary>
    Default,

    /// <summary>Full screen: no border, square corners.</summary>
    None,

    /// <summary>The mini player: no border, round corners.</summary>
    Round,
}

/// <summary>
/// Owns the window's DWM border and corners. Windows (and WPF-UI's theme code) put the default 1-pixel border back
/// when the window is activated again or the theme changes, which drew a light line around the full-screen video
/// after Alt+Tab. So the chosen style is applied again after each of those.
/// </summary>
public sealed class WindowEdge
{
    private static readonly HashSet<int> ResetMessages =
    [
        NativeMethods.WmActivate,
        NativeMethods.WmNcActivate,
        NativeMethods.WmSettingChange,
        NativeMethods.WmThemeChanged,
        NativeMethods.WmDwmCompositionChanged,
        NativeMethods.WmDwmColorizationColorChanged,
    ];

    private readonly Window _window;
    private bool _reapplyQueued;

    public WindowEdge(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(Hook);
    }

    public EdgeStyle Style { get; private set; }

    public void Set(EdgeStyle style)
    {
        Style = style;
        Apply();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // After the message is fully handled, so nothing that runs later can undo it.
        if (Style != EdgeStyle.Default && ResetMessages.Contains(msg) && !_reapplyQueued)
        {
            _reapplyQueued = true;
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                _reapplyQueued = false;
                Apply();
            });
        }

        return 0;
    }

    private void Apply()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        var border = Style == EdgeStyle.Default ? NativeMethods.BorderDefault : NativeMethods.BorderNone;
        var corners = (uint)(Style == EdgeStyle.None ? NativeMethods.CornerSquare : NativeMethods.CornerRound);
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmBorderColor, ref border, sizeof(uint));
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmCornerPreference, ref corners, sizeof(uint));
    }
}
