using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Retune.App.Composition;
using Retune.Core.Models;
using Retune.Presentation.ViewModels;

namespace Retune.App.Dev;

/// <summary>
/// Dev-only <c>--tour &lt;folder&gt; --demo &lt;url&gt;</c>: walks the real window through the main screens without any
/// input and saves each as a PNG (for the README). Run it muted, with the showcase media behind the provider.
/// </summary>
internal static class ScreenshotTour
{
    private const double TourZoom = 1.25;

    public static async Task RunAsync(AppContainer app, MainWindow window, string provider, string folder)
    {
        Directory.CreateDirectory(folder);
        var shell = app.Shell;
        var playlist = new Playlist(Guid.NewGuid(), "Demo TV", new XtreamSource(provider, "demo"), DateTimeOffset.UtcNow);
        await app.Playlists.AddAsync(playlist, "demo").ConfigureAwait(true);
        try
        {
            async Task Shot(string name, double settleSeconds)
            {
                await Task.Delay(TimeSpan.FromSeconds(settleSeconds)).ConfigureAwait(true);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                WindowCapture.SavePng(window, Path.Combine(folder, name));
            }

            // A little history so favourites, badges and Continue Watching look lived-in.
            DemoHistory.Seed(app, playlist.Id);

            // The same comfortable size on any screen: 125%, as on a typical laptop display.
            shell.SetZoom(TourZoom / VisualTreeHelper.GetDpi(window).DpiScaleX);

            ShowAll(shell, LibrarySection.Movies);
            shell.SetLayout(LayoutMode.Grid);

            await Shot("browse-movies.png", 4);

            shell.Open(shell.Content.Items[7]);
            await Shot("movie-detail.png", 3);
            shell.Back();

            ShowAll(shell, LibrarySection.Series);
            shell.SetLayout(LayoutMode.Grid);
            await Shot("browse-series.png", 3);
            shell.Open(shell.Content.Items[4]);
            await Shot("series-detail.png", 3.5);
            shell.Back();

            shell.SelectSection(LibrarySection.Live);
            shell.SetLayout(LayoutMode.List);
            await Shot("live-tv.png", 3);

            shell.Open(shell.Content.Items[2]);
            await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(true);
            var player = app.PlayerModel!;
            player.ToggleList();
            player.UserActivity();
            await Shot("player-live.png", 1.2);
            player.ToggleList();
            player.Close();

            ShowAll(shell, LibrarySection.Movies);
            shell.Open(shell.Content.Items[12]);
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
            ((MovieDetailViewModel)shell.Detail!).PlayCommand.Execute(null);
            await Task.Delay(TimeSpan.FromSeconds(7)).ConfigureAwait(true);
            player.UserActivity();
            await Shot("player-movie.png", 1.2);
            player.ToggleShortcuts();
            await Shot("player-shortcuts.png", 1);
            player.Close();
            shell.Back();

            shell.SearchText = "midnight";
            await Shot("search.png", 3);
            shell.SearchText = string.Empty;

            shell.OpenSettings();
            await Shot("settings.png", 1.5);
            shell.CloseSettings();
        }
        finally
        {
            await app.Playlists.DeleteAsync(playlist.Id).ConfigureAwait(true);
            window.Close();
        }
    }

    /// <summary>A section's "All" list (with history, Movies and Series would open on Continue Watching).</summary>
    private static void ShowAll(ShellViewModel shell, LibrarySection section)
    {
        shell.SelectSection(section);
        shell.SelectCategory(shell.Categories.Fixed[0]);
    }
}
