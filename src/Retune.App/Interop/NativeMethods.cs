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
    internal const int WmGetObject = 0x003D;
    internal const int WmActivate = 0x0006;
    internal const int WmActivateApp = 0x001C;
    internal const int WmSettingChange = 0x001A;
    internal const int WmMouseActivate = 0x0021;
    internal const int WmNcActivate = 0x0086;
    internal const int WmThemeChanged = 0x031A;
    internal const int WmDwmCompositionChanged = 0x031E;
    internal const int WmDwmColorizationColorChanged = 0x0320;
    internal const int MaNoActivate = 3;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    /// <summary>
    /// Whether a window of this process is the one Windows has in front. WPF's Window.IsActive can read true again
    /// right after the user switched away (the video engine's window thread re-activates it inside the app), so
    /// decisions about "the user left / came back" ask Windows instead.
    /// </summary>
    internal static bool IsThisAppInFront()
    {
        var front = GetForegroundWindow();
        return front != 0 && GetWindowThreadProcessId(front, out var pid) != 0 && pid == (uint)Environment.ProcessId;
    }

    internal static readonly nint HwndTop = 0;
    internal static readonly nint HwndTopmost = -1;

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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hwnd);

    /// <summary>Restores and focuses the already-running Retune window.</summary>
    /// <summary>
    /// Brings the already running Retune (same user session) forward: found by its process, not its title (an Explorer
    /// window on Pictures\Retune has that title too), and restored only when minimised, so a maximised window stays so.
    /// </summary>
    internal static void ActivateExistingWindow()
    {
        const int SwRestore = 9;
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var other in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id == current.Id || other.SessionId != current.SessionId || other.MainWindowHandle == 0)
                {
                    continue;
                }

                if (IsIconic(other.MainWindowHandle))
                {
                    ShowWindow(other.MainWindowHandle, SwRestore);
                }

                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint hwnd, uint flags);

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE (Windows 11).</summary>
    internal const int DwmCornerPreference = 33;

    /// <summary>DWMWA_BORDER_COLOR (Windows 11).</summary>
    internal const int DwmBorderColor = 34;

    internal const int CornerSquare = 1;
    internal const int CornerRound = 2;
    internal const uint BorderNone = 0xFFFFFFFE;
    internal const uint BorderDefault = 0xFFFFFFFF;

    /// <summary>Returns an HRESULT; older Windows versions reject the Windows 11 attributes, which is harmless.</summary>
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out uint value, int size);
    [StructLayout(LayoutKind.Sequential)]
    internal struct CursorPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>The pointer in physical screen pixels.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out CursorPoint point);

    /// <summary>
    /// Where a window is, exactly as Windows records it (normal bounds, maximised or not), so it can be put back later
    /// without being activated. Opaque outside this class.
    /// </summary>
    internal readonly struct SavedPlacement
    {
        private readonly NativeWindowPlacement _value;

        private SavedPlacement(NativeWindowPlacement value) => _value = value;

        public bool IsMaximized => _value.ShowCmd == SwShowMaximized || (_value.ShowCmd == SwShowMinimized && (_value.Flags & RestoreToMaximized) != 0);

        public static SavedPlacement? Capture(nint hwnd)
        {
            var wp = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
            return hwnd != 0 && GetWindowPlacement(hwnd, ref wp) ? new SavedPlacement(wp) : null;
        }

        /// <summary>
        /// Puts the window back. Restoring a normal window never activates it; restoring a maximised one does (Windows
        /// has no "maximise without activating"), so callers do that only when Retune is in front or about to be.
        /// With <paramref name="minimized"/> the window goes to the taskbar instead, still remembering to come back
        /// maximised, and is not activated either way.
        /// </summary>
        public void Restore(nint hwnd, bool minimized)
        {
            var wp = _value;
            wp.Length = Marshal.SizeOf<NativeWindowPlacement>();
            var maximized = IsMaximized;
            wp.Flags = maximized ? RestoreToMaximized : 0;
            wp.ShowCmd = minimized ? SwShowMinNoActive : maximized ? SwShowMaximized : SwShowNoActivate;
            _ = SetWindowPlacement(hwnd, ref wp);
        }

        /// <summary>Un-maximises (or shows) the window at its normal bounds without activating it.</summary>
        public static void ShowNormalWithoutActivating(nint hwnd)
        {
            var wp = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
            if (GetWindowPlacement(hwnd, ref wp))
            {
                wp.Flags = 0;
                wp.ShowCmd = SwShowNoActivate;
                _ = SetWindowPlacement(hwnd, ref wp);
            }
        }
    }

    private const int SwShowNoActivate = 4;
    private const int SwShowMinNoActive = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int SwShowNormal = 1;
    private const int SwShowMinimized = 2;
    private const int SwShowMaximized = 3;
    private const int RestoreToMaximized = 2;

    /// <summary>The window's normal (restored) bounds in physical workspace pixels and whether it is maximised.</summary>
    internal static Retune.Core.Models.WindowPlacement? GetPlacement(nint hwnd)
    {
        var wp = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
        if (hwnd == 0 || !GetWindowPlacement(hwnd, ref wp))
        {
            return null;
        }

        var maximized = wp.ShowCmd == SwShowMaximized || (wp.ShowCmd == SwShowMinimized && (wp.Flags & RestoreToMaximized) != 0);
        return new Retune.Core.Models.WindowPlacement(wp.Left, wp.Top, wp.Right - wp.Left, wp.Bottom - wp.Top, maximized);
    }

    /// <summary>Applies saved bounds; Windows moves the window onto a connected screen if that spot is gone.</summary>
    internal static void SetPlacement(nint hwnd, Retune.Core.Models.WindowPlacement placement)
    {
        var wp = new NativeWindowPlacement
        {
            Length = Marshal.SizeOf<NativeWindowPlacement>(),
            ShowCmd = placement.Maximized ? SwShowMaximized : SwShowNormal,
            Left = (int)Math.Round(placement.Left),
            Top = (int)Math.Round(placement.Top),
            Right = (int)Math.Round(placement.Left + placement.Width),
            Bottom = (int)Math.Round(placement.Top + placement.Height),
        };
        _ = SetWindowPlacement(hwnd, ref wp);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(nint hwnd, ref NativeWindowPlacement placement);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(nint hwnd, ref NativeWindowPlacement placement);
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
