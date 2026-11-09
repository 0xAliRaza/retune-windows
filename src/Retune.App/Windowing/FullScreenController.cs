using System.Windows;
using System.Windows.Interop;
using Retune.App.Interop;

namespace Retune.App.Windowing;

/// <summary>
/// Borderless full screen over the window's current monitor (taskbar included), restoring the exact
/// previous placement (normal bounds, maximised or not) on exit. Works with WPF-UI's custom-chrome windows. Windows 11
/// draws a 1-pixel border and rounds the corners of every window, even one the size of the monitor, so both are
/// switched off while in full screen (<see cref="WindowEdge"/> keeps them off). Nothing here activates the window, so
/// it also works while another app is in front.
/// </summary>
public sealed class FullScreenController(Window window, WindowEdge edge)
{
    private NativeMethods.SavedPlacement? _before;
    private ResizeMode _restoreResizeMode;

    public bool IsFullScreen { get; private set; }

    /// <summary>Raised after entering or leaving full screen.</summary>
    public event EventHandler? Changed;

    private nint Handle => new WindowInteropHelper(window).Handle;

    public void Enter()
    {
        if (IsFullScreen)
        {
            return;
        }

        var hwnd = Handle;
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (NativeMethods.SavedPlacement.Capture(hwnd) is not { } before
            || !NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest), ref info))
        {
            return;
        }

        _before = before;
        _restoreResizeMode = window.ResizeMode;
        IsFullScreen = true;
        edge.Set(EdgeStyle.None);
        window.ResizeMode = ResizeMode.NoResize;
        if (before.IsMaximized)
        {
            NativeMethods.SavedPlacement.ShowNormalWithoutActivating(hwnd);
        }

        var m = info.Monitor;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTop, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, NativeMethods.SwpFrameChanged | NativeMethods.SwpNoActivate);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the placement from before; with <paramref name="minimized"/>, straight to the taskbar instead.</summary>
    public void Exit(bool minimized = false)
    {
        if (!IsFullScreen)
        {
            return;
        }

        IsFullScreen = false;
        window.ResizeMode = _restoreResizeMode;
        _before?.Restore(Handle, minimized);
        _before = null;
        edge.Set(EdgeStyle.Default);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
