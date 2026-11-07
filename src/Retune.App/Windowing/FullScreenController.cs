using System.Windows;
using System.Windows.Interop;
using Retune.App.Interop;

namespace Retune.App.Windowing;

/// <summary>
/// Borderless full screen over the window's current monitor (taskbar included), restoring the exact
/// previous bounds and state on exit. Works with WPF-UI's custom-chrome windows.
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
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
