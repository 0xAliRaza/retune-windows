using System.Runtime.InteropServices;

namespace Retune.App.Interop;

/// <summary>The few Win32 calls the WPF layer needs for native video hosting and full screen.</summary>
internal static partial class NativeMethods
{
    internal const int WsChild = 0x40000000;
    internal const int WsVisible = 0x10000000;
    internal const int WsClipChildren = 0x02000000;
    internal const int WsClipSiblings = 0x04000000;
    internal const int SsBlackRect = 0x00000004;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpNoOwnerZOrder = 0x0200;

    internal const uint MonitorDefaultToNearest = 0x00000002;

    internal const int WmSetFocus = 0x0007;

    internal static readonly nint HwndTop = 0;

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDoubleClickTime();

    internal const uint EsContinuous = 0x80000000;
    internal const uint EsSystemRequired = 0x00000001;
    internal const uint EsDisplayRequired = 0x00000002;

    [LibraryImport("kernel32.dll")]
    internal static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string? className, string windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hwnd);

    /// <summary>Restores and focuses the already-running Retune window.</summary>
    internal static void ActivateExistingWindow(string title)
    {
        const int SwRestore = 9;
        var hwnd = FindWindow(null, title);
        if (hwnd != 0)
        {
            ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        }
    }

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }
}
