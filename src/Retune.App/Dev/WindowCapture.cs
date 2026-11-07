using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Retune.App.Dev;

/// <summary>
/// Dev-only: saves the main window as a PNG without reading the screen, so it works while the PC is locked or the
/// window is covered. The WPF content is rendered off-screen at the window's DPI; the mpv video comes from its
/// native window (PrintWindow); the player chrome is rendered on top. Mica can't be captured, so the backdrop is
/// its dark fallback colour.
/// </summary>
internal static partial class WindowCapture
{
    private const uint RenderFullContent = 2;
    private static readonly Color Backdrop = Color.FromRgb(0x20, 0x20, 0x20);

    public static void SavePng(MainWindow window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var size = new Size(root.ActualWidth, root.ActualHeight);
        var playing = window.PlayerLayer.Visibility == Visibility.Visible;

        var scene = new DrawingVisual();
        using (var dc = scene.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Backdrop), null, new Rect(size));
            if (playing && window.Video.VideoWindow != 0 && CaptureNative(window.Video.VideoWindow) is { } video)
            {
                dc.DrawImage(video, Bounds(window.Video, root));
            }

            // While playing, the video window covers the browse shell, so only the title bar is drawn from WPF.
            if (playing)
            {
                dc.DrawImage(Render(window.AppTitleBar, dpi), Bounds(window.AppTitleBar, root));
            }
            else
            {
                dc.DrawImage(Render(root, dpi), new Rect(size));
            }
            if (playing && window.Overlay.Visibility == Visibility.Visible)
            {
                dc.DrawImage(Render(window.PlayerChrome, dpi), Bounds(window.Overlay, root));
            }
        }

        var bitmap = new RenderTargetBitmap(Pixels(size.Width, dpi.DpiScaleX), Pixels(size.Height, dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static int Pixels(double dips, double scale) => (int)Math.Round(dips * scale);

    private static Rect Bounds(FrameworkElement element, UIElement root) =>
        new(element.TranslatePoint(new Point(0, 0), root), new Size(element.ActualWidth, element.ActualHeight));

    private static RenderTargetBitmap Render(FrameworkElement element, DpiScale dpi)
    {
        var bitmap = new RenderTargetBitmap(Pixels(element.ActualWidth, dpi.DpiScaleX), Pixels(element.ActualHeight, dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    /// <summary>A native child window's pixels (the mpv video), or null when Windows can't provide them.</summary>
    private static BitmapSource? CaptureNative(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0)
        {
            return null;
        }

        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, client.Right, client.Bottom);
        var previous = SelectObject(memory, bitmap);
        try
        {
            var printed = PrintWindow(hwnd, memory, RenderFullContent);
            SelectObject(memory, previous); // the bitmap must leave the DC before its pixels can be read
            previous = 0;
            return printed ? Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()) : null;
        }
        finally
        {
            if (previous != 0)
            {
                SelectObject(memory, previous);
            }

            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out NativeRect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);
}
