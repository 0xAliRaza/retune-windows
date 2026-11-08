using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private static readonly Size Page = new(1600, 1000);

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

            await Snap(Dialog(shell, new Dialogs.ShortcutsDialog(_ => { })), Path.Combine(folder, "9-shortcuts.png")).ConfigureAwait(true);
            app.Shell.SearchText = string.Empty;
            var hidden = app.Playlists.Index!.Catalog.CategoriesFor(LibrarySection.Movies)[1].Id;
            app.Library.Update(s => Core.Library.LibraryOperations.ToggleHidden(s, Core.Library.LibraryKeys.Category(playlist.Id, LibrarySection.Movies, hidden)));
            app.Shell.OpenSettings();
            await Snap(shell, Path.Combine(folder, "10-settings.png"), settle: 1000).ConfigureAwait(true);
            app.Shell.CloseSettings();

            // App-wide zoom, applied the way MainWindow applies it.
            app.Shell.SelectSection(LibrarySection.Movies);
            app.Shell.SetLayout(LayoutMode.Grid);
            shell.LayoutTransform = new ScaleTransform(1.25, 1.25);
            await Snap(shell, Path.Combine(folder, "11-zoom-125.png"), settle: 2500).ConfigureAwait(true);
            shell.LayoutTransform = Transform.Identity;

            app.Shell.ToggleSidebar();
            await Snap(shell, Path.Combine(folder, "12-sidebar-collapsed.png"), settle: 1500).ConfigureAwait(true);
            app.Shell.ToggleSidebar();

            await PlayerPreview.RenderAsync(app, folder).ConfigureAwait(true);
        }
        finally
        {
            await app.Playlists.DeleteAsync(playlist.Id).ConfigureAwait(true);
        }
    }

    /// <summary>A dialog card over a dimmed copy of the page, as the main window's dialog layer draws it.</summary>
    private static Grid Dialog(FrameworkElement page, FrameworkElement dialog)
    {
        var card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.FindResource("SolidBackgroundFillColorTertiaryBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("SurfaceStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = dialog,
        };
        var root = new Grid { Background = new VisualBrush(page) };
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) });
        root.Children.Add(card);
        return root;
    }

    private static Task Snap(FrameworkElement element, string path, int settle = 300) => Snap(element, path, Page, settle);

    /// <summary>Lays <paramref name="element"/> out at <paramref name="size"/>, lets bindings and images settle, then saves it as a PNG.</summary>
    internal static async Task Snap(FrameworkElement element, string path, Size size, int settle)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(settle);
        while (DateTime.UtcNow < deadline)
        {
            element.Measure(size);
            element.Arrange(new Rect(size));
            element.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(50).ConfigureAwait(true);
        }

        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var file = File.Create(path);
        encoder.Save(file);
    }
}
