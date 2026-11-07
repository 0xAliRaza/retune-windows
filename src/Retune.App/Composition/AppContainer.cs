using System.Windows;
using System.Windows.Threading;
using Retune.Core.Diagnostics;
using Retune.Core.Playback;
using Retune.Infrastructure.Diagnostics;
using Retune.Core.Sources;
using Retune.Mpv;
using Retune.Presentation.Player;
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
        Log = new FileAppLog(Paths.LogDirectory, Time);
        void Post(Action action) => dispatcher.BeginInvoke(action);

        var data = new JsonFileStore(Paths.DataDirectory, Time);
        var protectedData = new JsonFileStore(Paths.DataDirectory, Time, encrypt: true);
        var playlists = new PlaylistStore(protectedData);
        Settings = new SettingsService(new SettingsStore(data), Time);
        _catalogs = new CatalogCache(Paths.CatalogDirectory, Time);
        var secrets = new CredentialManagerSecretStore(Paths.SecretPrefix);

        _apiFetcher = new HttpFetcher(maxConnectionsPerServer: 4, UserAgent);
        _artworkFetcher = new HttpFetcher(maxConnectionsPerServer: 8, UserAgent, idleTimeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(60));
        Artwork = new ArtworkLoader(new ArtworkCache(Paths.ArtworkDirectory, _artworkFetcher, time: Time));

        Library = new LibraryService(new LibraryStore(data), Time);
        var source = new CatalogSource(_apiFetcher, new LocalFileReader(), secrets, Time);
        Playlists = new PlaylistController(playlists, _catalogs, secrets, Settings, source, Library, Time, Log);
        Dialogs = new WpfDialogs(this);
        Player = new PlayerLauncher();
        Playlists.ActivePlaylistChanging += (_, _) => Player.StopForPlaylistChange();
        Shell = new ShellViewModel(Playlists, Library, Settings, Dialogs, Player, Time, Post, () => new SettingsViewModel(Settings, Library, Playlists, Dialogs, ClearArtworkInBackground));
    }

    public AppPaths Paths { get; }

    /// <summary>Deleting thousands of cached files must not freeze the window.</summary>
    private void ClearArtworkInBackground() => Task.Run(Artwork.Clear).Forget(Log, "artwork cache clear");

    public TimeProvider Time { get; }

    public IAppLog Log { get; }

    public SettingsService Settings { get; }

    public ArtworkLoader Artwork { get; }

    public LibraryService Library { get; }

    public PlaylistController Playlists { get; }

    public WpfDialogs Dialogs { get; }

    public PlayerLauncher Player { get; }

    public ShellViewModel Shell { get; }

    /// <summary>The container of the running app, for views that need services (artwork, dialogs).</summary>
    public static AppContainer Current => ((App)Application.Current).Container;

    public PlayerViewModel? PlayerModel { get; private set; }

    /// <summary>Dev runs: no audio device at all, so nothing can be heard (the UI still shows the real volume).</summary>
    public bool Silent { get; set; }

    private MpvClient? _mpv;
    private MpvEngine? _engine;
    private PlaybackSession? _session;

    /// <summary>
    /// Creates the player once the video window exists (mpv must know it before initialising), then replays a
    /// play request that arrived earlier (the load gate).
    /// </summary>
    public PlayerViewModel AttachVideo(nint videoWindow, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        void Post(Action action) => dispatcher.BeginInvoke(action);
        var volume = (int)Core.Playback.VolumeLevel.Clamped(Settings.Current.Volume);
        _mpv = MpvClient.Create(MpvOptions.ForEmbeddedPlayer(videoWindow, volume, "Retune/0.1 (Windows; libmpv)", Silent), logLevel: null);
        _engine = new MpvEngine(_mpv, Post);
        _session = new PlaybackSession(_engine, new LibraryPlaybackObserver(Library, Playlists), Time, Post);
        PlayerModel = new PlayerViewModel(_engine, _session, Playlists, Settings, new KeepAwake(), Time, Post);
        Player.Attach(PlayerModel);
        if (Player.LastRequest is { } pending)
        {
            PlayerModel.Play(pending);
        }

        return PlayerModel;
    }

    /// <summary>Saves the final progress and destroys mpv. Must run before the video window is destroyed.</summary>
    public void ShutdownPlayer()
    {
        PlayerModel?.Close();
        PlayerModel?.Dispose();
        _session?.Dispose();
        _engine?.Dispose();
        _mpv?.Dispose();
        PlayerModel = null;
        _session = null;
        _engine = null;
        _mpv = null;
        Library.Flush();
    }

    public void Dispose()
    {
        ShutdownPlayer();
        Library.Flush();
        Shell.Dispose();
        Library.Dispose();
        _catalogs.Dispose();
        _apiFetcher.Dispose();
        _artworkFetcher.Dispose();
        Settings.Dispose();
    }
}
