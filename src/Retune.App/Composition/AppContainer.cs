using System.Windows;
using System.Windows.Threading;
using Retune.Core.Sources;
using Retune.Infrastructure.Net;
using Retune.Infrastructure.Secrets;
using Retune.Infrastructure.Storage;
using Retune.Presentation.Services;
using Retune.Presentation.ViewModels;
using Retune.App.Services;

namespace Retune.App.Composition;

/// <summary>
/// The composition root: builds every service once, in dependency order, and disposes them in reverse.
/// Plain constructor injection — the graph is small and explicit, so no container is needed.
/// </summary>
public sealed class AppContainer : IDisposable
{
    public const string UserAgent = "Retune/0.1 (Windows)";

    private readonly HttpFetcher _apiFetcher;
    private readonly HttpFetcher _artworkFetcher;
    private readonly CatalogCache _catalogs;

    public AppContainer(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        Paths = AppPaths.ForCurrentUser();
        Time = TimeProvider.System;
        void Post(Action action) => dispatcher.BeginInvoke(action);

        var data = new JsonFileStore(Paths.DataDirectory, Time);
        var protectedData = new JsonFileStore(Paths.DataDirectory, Time, encrypt: true);
        var playlists = new PlaylistStore(protectedData);
        Settings = new SettingsStore(data);
        _catalogs = new CatalogCache(Paths.CatalogDirectory, Time);
        var secrets = new CredentialManagerSecretStore();

        _apiFetcher = new HttpFetcher(maxConnectionsPerServer: 4, UserAgent);
        _artworkFetcher = new HttpFetcher(maxConnectionsPerServer: 8, UserAgent, idleTimeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(60));
        Artwork = new ArtworkLoader(new ArtworkCache(Paths.ArtworkDirectory, _artworkFetcher, time: Time));

        Library = new LibraryService(new LibraryStore(data), Time);
        var source = new CatalogSource(_apiFetcher, new LocalFileReader(), secrets, Time);
        Playlists = new PlaylistController(playlists, _catalogs, secrets, Settings, source, Library, Time);
        Dialogs = new WpfDialogs(this);
        Player = new PlayerLauncher();
        Playlists.ActivePlaylistChanging += (_, _) => Player.StopForPlaylistChange();
        Shell = new ShellViewModel(Playlists, Library, Settings, Dialogs, Player, Time, Post);
    }

    public AppPaths Paths { get; }

    public TimeProvider Time { get; }

    public SettingsStore Settings { get; }

    public ArtworkLoader Artwork { get; }

    public LibraryService Library { get; }

    public PlaylistController Playlists { get; }

    public WpfDialogs Dialogs { get; }

    public PlayerLauncher Player { get; }

    public ShellViewModel Shell { get; }

    /// <summary>The container of the running app, for views that need services (artwork, dialogs).</summary>
    public static AppContainer Current => ((App)Application.Current).Container;

    public void Dispose()
    {
        Library.Flush();
        Shell.Dispose();
        Library.Dispose();
        _catalogs.Dispose();
        _apiFetcher.Dispose();
        _artworkFetcher.Dispose();
    }
}
