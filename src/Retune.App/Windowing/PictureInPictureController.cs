using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Retune.App.Interop;
using Retune.Presentation.Player;

namespace Retune.App.Windowing;

/// <summary>
/// Turns the main window into the mini player: small, always on top, in the bottom-right corner of its screen, and
/// back again to exactly where it was (normal, maximised or full screen). The one video window moves with it, so
/// playback never restarts.
/// </summary>
/// <remarks>
/// It opens when the user switches to another app, so it must never activate the window: an activation would pull
/// Retune back in front, which counts as "coming back" and closes the mini player again. Every move here is a
/// non-activating placement, and while small, clicks don't activate the window either, so using the mini player leaves
/// the keyboard with the app the user switched to.
/// </remarks>
public sealed class PictureInPictureController
{
    private readonly Window _window;
    private readonly WindowEdge _edge;
    private NativeMethods.SavedPlacement? _before;
    private EdgeStyle _restoreEdge;
    private ResizeMode _restoreResizeMode;
    private double _restoreMinWidth;
    private double _restoreMinHeight;
    private bool _restoreTopmost;

    public PictureInPictureController(Window window, WindowEdge edge)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _edge = edge ?? throw new ArgumentNullException(nameof(edge));
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(Handle)?.AddHook(Hook);
    }

    public bool IsActive { get; private set; }

    /// <summary>Raised after entering or leaving the mini player.</summary>
    public event EventHandler? Changed;

    private nint Handle => new WindowInteropHelper(_window).Handle;

    public void Enter()
    {
        var hwnd = Handle;
        if (IsActive || hwnd == 0)
        {
            return;
        }

        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (NativeMethods.SavedPlacement.Capture(hwnd) is not { } before
            || !NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest), ref info))
        {
            return;
        }

        _before = before;
        _restoreEdge = _edge.Style;
        _restoreResizeMode = _window.ResizeMode;
        _restoreMinWidth = _window.MinWidth;
        _restoreMinHeight = _window.MinHeight;
        _restoreTopmost = _window.Topmost;

        var work = info.WorkArea;
        var target = PictureInPicture.Place(new PixelBounds(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), VisualTreeHelper.GetDpi(_window).DpiScaleX);
        IsActive = true;
        _edge.Set(EdgeStyle.Round);
        _window.MinWidth = 0;
        _window.MinHeight = 0;
        _window.ResizeMode = ResizeMode.NoResize;
        if (before.IsMaximized)
        {
            NativeMethods.SavedPlacement.ShowNormalWithoutActivating(hwnd);
        }

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, target.X, target.Y, target.Width, target.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
        _window.Topmost = true; // keeps WPF's idea in step with the window
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the placement from before; with <paramref name="minimized"/>, straight to the taskbar instead.</summary>
    public void Exit(bool minimized = false)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _window.Topmost = _restoreTopmost;
        _window.MinWidth = _restoreMinWidth;
        _window.MinHeight = _restoreMinHeight;
        _window.ResizeMode = _restoreResizeMode;
        _before?.Restore(Handle, minimized);
        _before = null;
        _edge.Set(_restoreEdge);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (IsActive && msg == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return NativeMethods.MaNoActivate;
        }

        return 0;
    }
}
