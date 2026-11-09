using System.IO;
using System.Security.Cryptography;
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

    /// <summary>mpv warnings kept per minute (a broken stream can print hundreds a second).</summary>
    private const int MpvLogLinesPerMinute = 30;

    private const string TrustedCertificatesFile = "trusted-roots.pem";

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
        Settings = new SettingsService(new SettingsStore(data), Time, Log);
        _catalogs = new CatalogCache(Paths.CatalogDirectory, Time);
        var secrets = new CredentialManagerSecretStore(Paths.SecretPrefix);

        _apiFetcher = new HttpFetcher(maxConnectionsPerServer: 4, UserAgent);
        _artworkFetcher = new HttpFetcher(maxConnectionsPerServer: 8, UserAgent, idleTimeout: TimeSpan.FromSeconds(20), totalTimeout: TimeSpan.FromSeconds(60));
        Artwork = new ArtworkLoader(new ArtworkCache(Paths.ArtworkDirectory, _artworkFetcher, time: Time));

        Library = new LibraryService(new LibraryStore(data), Time, Log);
        var source = new CatalogSource(_apiFetcher, new LocalFileReader(), secrets, Time);
        Playlists = new PlaylistController(playlists, _catalogs, secrets, Settings, source, Library, Time, Log);
        Dialogs = new WpfDialogs(this);
        Player = new PlayerLauncher();
        Playlists.ActivePlaylistChanging += (_, _) => Player.StopForPlaylistChange();
        Shell = new ShellViewModel(Playlists, Library, Settings, Dialogs, Player, Time, Post, () => new SettingsViewModel(Settings, Library, Playlists, Dialogs, ClearArtworkInBackground));
    }

    public AppPaths Paths { get; }

    /// <summary>
    /// Refreshes the roots mpv checks HTTPS streams against from Windows' stores, on every start so Windows Update's
    /// changes apply. Without the file every HTTPS stream is refused (never played unverified) and says why.
    /// </summary>
    private string? WriteTrustedCertificates()
    {
        var path = Path.Combine(Paths.DataDirectory, TrustedCertificatesFile);
        try
        {
            SystemTrustStore.WriteBundle(path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Log.Failure("player", "Couldn't export Windows' trusted certificates; HTTPS streams can't be verified.", ex);
            return null;
        }
    }

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
        var options = MpvOptions.ForEmbeddedPlayer(videoWindow, volume, "Retune/0.1 (Windows; libmpv)", Silent, WriteTrustedCertificates());
        _mpv = MpvClient.Create(options, logLevel: "warn");
        var mpvLogLimit = new LogRateLimiter(MpvLogLinesPerMinute, TimeSpan.FromMinutes(1), Time);
        _mpv.EventReceived += (_, e) =>
        {
            // Runs on mpv's thread; the log is thread-safe, strips URLs and credentials, and is rate-limited here.
            if (e is MpvLogMessage message && mpvLogLimit.Allow(out var dropped))
            {
                if (dropped > 0)
                {
                    Log.Warning("mpv", $"{dropped} more mpv messages were not logged.");
                }

                Log.Warning("mpv", $"{message.Prefix}: {message.Text.TrimEnd()}");
            }
        };
        _engine = new MpvEngine(_mpv, Post);
        _session = new PlaybackSession(_engine, new LibraryPlaybackObserver(Library, Playlists), Time, Post);
        PlayerModel = new PlayerViewModel(_engine, _session, Playlists, Library, Settings, new KeepAwake(), Time, Post, Log);
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
        Playlists.Dispose();
        Library.Dispose();
        _catalogs.Dispose();
        _apiFetcher.Dispose();
        _artworkFetcher.Dispose();
        Settings.Dispose();
    }
}
