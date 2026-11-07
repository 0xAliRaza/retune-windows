using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Retune.App.Interop;

namespace Retune.App.Video;

/// <summary>
/// A black native child window that mpv renders into (<c>wid</c>). mpv draws on its own thread with
/// its own D3D11 swap chain, so the WPF UI thread can never stall the video.
/// The app keeps one instance for its lifetime so the video surface survives the player closing.
/// </summary>
public sealed class VideoHost : HwndHost
{
    /// <summary>The native window mpv renders into, once created.</summary>
    public nint VideoWindow { get; private set; }

    /// <summary>Raised once the native window exists and can be handed to mpv.</summary>
    public event EventHandler? VideoWindowCreated;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var hwnd = NativeMethods.CreateWindowEx(
            0, "static", string.Empty,
            NativeMethods.WsChild | NativeMethods.WsVisible | NativeMethods.WsClipChildren | NativeMethods.WsClipSiblings | NativeMethods.SsBlackRect,
            0, 0, 1, 1, hwndParent.Handle, 0, 0, 0);
        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Creating the video window failed.");
        }

        VideoWindow = hwnd;
        Dispatcher.BeginInvoke(() => VideoWindowCreated?.Invoke(this, EventArgs.Empty));
        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        NativeMethods.DestroyWindow(hwnd.Handle);
        VideoWindow = 0;
    }
}
