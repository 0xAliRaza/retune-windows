using Microsoft.Extensions.Time.Testing;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Persistence;
using Retune.Core.Playback;
using Retune.Core.Sources;
using Retune.FakeProvider;
using Retune.Infrastructure.Net;
using Retune.Infrastructure.Storage;
using Retune.Presentation.Services;
using Retune.Presentation.ViewModels;

namespace Retune.Presentation.Tests.Support;

/// <summary>The real controllers and view models over in-memory stores, a fake clock and the fake provider.</summary>
internal sealed class AppHarness : IAsyncDisposable
{
    private readonly HttpFetcher _fetcher = new(4, "Retune-Tests/1.0");

    private AppHarness(FakeProviderServer provider)
    {
        Provider = provider;
        Library = new LibraryService(LibraryStore, Time);
        SettingsService = new SettingsService(Settings, Time);
        var source = new CatalogSource(_fetcher, new LocalFileReader(), Secrets, TimeProvider.System);
        Controller = new PlaylistController(PlaylistStore, Catalogs, Secrets, SettingsService, source, Library, Time, Log);
        Shell = new ShellViewModel(Controller, Library, SettingsService, Dialogs, Player, Time, action => action(), () => new SettingsViewModel(SettingsService, Library, Controller, Dialogs, () => ArtworkClears++));
        Controller.ActivePlaylistChanging += (_, _) => Player.StopForPlaylistChange();
    }

    public FakeProviderServer Provider { get; }

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public MemoryStores.Playlists PlaylistStore { get; } = new();

    public MemoryStores.Library LibraryStore { get; } = new();

    public MemoryStores.Catalogs Catalogs { get; } = new();

    public MemoryStores.Secrets Secrets { get; } = new();

    public MemoryStores.Settings Settings { get; } = new();

    public MemoryLog Log { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public FakePlayer Player { get; } = new();

    public LibraryService Library { get; }

    public SettingsService SettingsService { get; }

    public int ArtworkClears { get; private set; }

    public PlaylistController Controller { get; }

    public ShellViewModel Shell { get; }

    public static async Task<AppHarness> StartAsync(FakeProviderOptions? options = null) =>
        new(await FakeProviderServer.StartAsync(options ?? new FakeProviderOptions()));

    public Playlist XtreamPlaylist(string name = "Demo") =>
        new(Guid.NewGuid(), name, new XtreamSource(Provider.BaseUrl, "demo"), Time.GetUtcNow());

    public async Task<Playlist> AddDemoAsync()
    {
        var playlist = XtreamPlaylist();
        await Controller.AddAsync(playlist, "demo");
        return playlist;
    }

    public async ValueTask DisposeAsync()
    {
        Shell.Dispose();
        Controller.Dispose();
        Library.Dispose();
        SettingsService.Dispose();
        _fetcher.Dispose();
        await Provider.DisposeAsync();
    }
}

internal static class MemoryStores
{
    internal sealed class Playlists : IPlaylistStore
    {
        public IReadOnlyList<Playlist> Saved { get; private set; } = [];

        public IReadOnlyList<Playlist> Load() => Saved;

        public void Save(IReadOnlyList<Playlist> playlists) => Saved = playlists;
    }

    internal sealed class Library : ILibraryStore
    {
        public LibraryState Saved { get; private set; } = LibraryState.Empty;

        public int SaveCount { get; private set; }

        public LibraryState Load() => Saved;

        public void Save(LibraryState state)
        {
            Saved = state;
            SaveCount++;
        }
    }

    internal sealed class Catalogs : ICatalogCache
    {
        public Dictionary<Guid, Catalog> Saved { get; } = [];

        public Task<Catalog?> LoadAsync(Guid playlistId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.GetValueOrDefault(playlistId));

        public bool FailSaves { get; set; }

        public Task SaveAsync(Guid playlistId, Catalog catalog)
        {
            if (FailSaves)
            {
                return Task.FromException(new IOException("disk full"));
            }

            Saved[playlistId] = catalog;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid playlistId)
        {
            Saved.Remove(playlistId);
            return Task.CompletedTask;
        }
    }

    internal sealed class Secrets : ISecretStore
    {
        public Dictionary<Guid, string> Saved { get; } = [];

        public string? Find(Guid playlistId) => Saved.GetValueOrDefault(playlistId);

        public void Store(Guid playlistId, string secret) => Saved[playlistId] = secret;

        public void Remove(Guid playlistId) => Saved.Remove(playlistId);
    }

    internal sealed class Settings : ISettingsStore
    {
        public AppSettings Saved { get; private set; } = AppSettings.Default;

        public AppSettings Load() => Saved;

        public void Save(AppSettings settings) => Saved = settings;
    }
}

/// <summary>Collects log entries so tests can assert a failure was reported.</summary>
internal sealed class MemoryLog : Core.Diagnostics.IAppLog
{
    public List<(string Level, string Area, Exception? Exception)> Entries { get; } = [];

    public void Warning(string area, string message, Exception? exception = null) => Add("WARN", area, exception);

    public void Failure(string area, string message, Exception? exception = null) => Add("ERROR", area, exception);

    private void Add(string level, string area, Exception? exception)
    {
        lock (Entries)
        {
            Entries.Add((level, area, exception));
        }
    }
}

internal sealed class FakeDialogs : IDialogs
{
    public bool ConfirmAnswer { get; set; } = true;

    public bool PinAnswer { get; set; } = true;

    public List<string> Confirmations { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive)
    {
        Confirmations.Add(title);
        return Task.FromResult(ConfirmAnswer);
    }

    public Task ShowPlaylistEditorAsync(Playlist? playlist) => Task.CompletedTask;

    public Task<bool> VerifyPinAsync(string reason) => Task.FromResult(PinAnswer);

    public Task<bool> CreatePinAsync() => Task.FromResult(PinAnswer);

    public int ShortcutSheets { get; private set; }

    public Task ShowShortcutsAsync()
    {
        ShortcutSheets++;
        return Task.CompletedTask;
    }

    public void OpenInBrowser(Uri url)
    {
    }
}

internal sealed class FakePlayer : IPlayerLauncher
{
    public event EventHandler? PlayingItemChanged;

    public List<(Lineup Lineup, bool Resume)> Played { get; } = [];

    public int Stops { get; private set; }

    public string? PlayingItemId { get; private set; }

    public void Play(Lineup lineup, bool fromResumePoint = true)
    {
        Played.Add((lineup, fromResumePoint));
        PlayingItemId = lineup.Items[lineup.StartIndex].Id;
        PlayingItemChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopForPlaylistChange() => Stops++;
}
