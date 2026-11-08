using System.Windows;
using System.Windows.Interop;
using Retune.App.Interop;

namespace Retune.App.Windowing;

/// <summary>
/// Borderless full screen over the window's current monitor (taskbar included), restoring the exact
/// previous bounds and state on exit. Works with WPF-UI's custom-chrome windows. Windows 11 draws a 1-pixel border
/// and rounds the corners of every window, even one the size of the monitor, so both are switched off while in
/// full screen.
/// </summary>
public sealed class FullScreenController(Window window)
{
    private Rect _restoreBounds;
    private WindowState _restoreState;
    private ResizeMode _restoreResizeMode;

    public bool IsFullScreen { get; private set; }

    /// <summary>Raised after entering or leaving full screen.</summary>
    public event EventHandler? Changed;

    public void Toggle()
    {
        if (IsFullScreen)
        {
            Exit();
        }
        else
        {
            Enter();
        }
    }

    public void Enter()
    {
        if (IsFullScreen)
        {
            return;
        }

        _restoreState = window.WindowState;
        _restoreBounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        _restoreResizeMode = window.ResizeMode;

        var hwnd = new WindowInteropHelper(window).Handle;
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest), ref info))
        {
            return;
        }

        IsFullScreen = true;
        SetEdge(hwnd, fullScreen: true);
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowState = WindowState.Normal;
        var m = info.Monitor;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTop, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, NativeMethods.SwpFrameChanged);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Exit()
    {
        if (!IsFullScreen)
        {
            return;
        }

        IsFullScreen = false;
        window.ResizeMode = _restoreResizeMode;
        window.Left = _restoreBounds.Left;
        window.Top = _restoreBounds.Top;
        window.Width = _restoreBounds.Width;
        window.Height = _restoreBounds.Height;
        window.WindowState = _restoreState;
        SetEdge(new WindowInteropHelper(window).Handle, fullScreen: false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>No border and square corners in full screen; otherwise the default border and the window's round corners.</summary>
    internal static void SetEdge(nint hwnd, bool fullScreen)
    {
        var border = fullScreen ? NativeMethods.BorderNone : NativeMethods.BorderDefault;
        var corners = (uint)(fullScreen ? NativeMethods.CornerSquare : NativeMethods.CornerRound);
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmBorderColor, ref border, sizeof(uint));
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmCornerPreference, ref corners, sizeof(uint));
    }
}
