using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Retune.App.Composition;
using Retune.App.Views;
using Retune.Core.Models;

namespace Retune.App.Dev;

/// <summary>
/// Renders the real UI to PNGs without ever showing a window, so visual checks can run while someone is using
/// the PC. Uses an isolated data folder (set RETUNE_DATA_DIR) and a demo provider, and cleans up its playlist.
/// </summary>
internal static class DevRenderer
{
    private const int Width = 1600;
    private const int Height = 1000;

    public static async Task RunAsync(AppContainer app, DevOptions options, string folder)
    {
        Directory.CreateDirectory(folder);
        var shell = new ShellView { DataContext = app.Shell, Background = (Brush)Application.Current.FindResource("ApplicationBackgroundBrush") };

        await Snap(shell, Path.Combine(folder, "0-welcome.png")).ConfigureAwait(true);
        if (options.DemoProvider is not { } provider)
        {
            return;
        }

        var playlist = new Playlist(Guid.NewGuid(), "Demo TV", new XtreamSource(provider, "demo"), DateTimeOffset.UtcNow);
        try
        {
            await app.Playlists.AddAsync(playlist, "demo").ConfigureAwait(true);
            await Snap(shell, Path.Combine(folder, "1-live-list.png"), settle: 2500).ConfigureAwait(true);

            app.Shell.SetLayout(LayoutMode.Grid);
            await Snap(shell, Path.Combine(folder, "2-live-grid.png"), settle: 2500).ConfigureAwait(true);

            app.Shell.SelectSection(LibrarySection.Movies);
            app.Shell.SelectCategory(app.Shell.Categories.Fixed[0]);
            await Snap(shell, Path.Combine(folder, "3-movies-grid.png"), settle: 3000).ConfigureAwait(true);

            app.Shell.SelectSection(LibrarySection.Series);
            app.Shell.SelectCategory(app.Shell.Categories.Fixed[0]);
            await Snap(shell, Path.Combine(folder, "4-series-grid.png"), settle: 3000).ConfigureAwait(true);

            app.Shell.Open(app.Shell.Content.Items[3]);
            await Snap(shell, Path.Combine(folder, "5-series-detail.png"), settle: 3500).ConfigureAwait(true);
            app.Shell.Back();

            app.Shell.SelectSection(LibrarySection.Movies);
            app.Shell.SelectCategory(app.Shell.Categories.Fixed[0]);
            app.Shell.Open(app.Shell.Content.Items[5]);
            await Snap(shell, Path.Combine(folder, "6-movie-detail.png"), settle: 3000).ConfigureAwait(true);
            app.Shell.Back();

            app.Shell.SetLayout(LayoutMode.Compact);
            await Snap(shell, Path.Combine(folder, "7-movies-compact.png"), settle: 1500).ConfigureAwait(true);

            app.Shell.SearchText = "midnight";
            await Snap(shell, Path.Combine(folder, "8-search.png"), settle: 1500).ConfigureAwait(true);
        }
        finally
        {
            await app.Playlists.DeleteAsync(playlist.Id).ConfigureAwait(true);
        }
    }

    private static async Task Snap(FrameworkElement element, string path, int settle = 300)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(settle);
        while (DateTime.UtcNow < deadline)
        {
            element.Measure(new Size(Width, Height));
            element.Arrange(new Rect(0, 0, Width, Height));
            element.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(100).ConfigureAwait(true);
        }

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var file = File.Create(path);
        encoder.Save(file);
    }
}
