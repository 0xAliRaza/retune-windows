using CommunityToolkit.Mvvm.ComponentModel;
using Retune.Core.Errors;
using Retune.Core.Models;
using Retune.Core.Persistence;
using Retune.Core.Search;
using Retune.Core.Sources;
using Retune.Core.Xtream;

namespace Retune.Presentation.Services;

/// <summary>
/// Owns the playlists and the active playlist's catalog: load from cache, refresh in the background, add,
/// edit, delete. Every async step checks a load generation so a stale refresh can never overwrite a newer
/// playlist or re-save a deleted one's cache. Spec: docs/spec/core.md §4.1. UI thread only.
/// </summary>
public sealed partial class PlaylistController(
    IPlaylistStore playlists,
    ICatalogCache cache,
    ISecretStore secrets,
    ISettingsStore settings,
    CatalogSource source,
    LibraryService library,
    TimeProvider time) : ObservableObject
{
    public const string DownloadingMessage = "Downloading channels, movies and series…";

    private int _generation;
    private (int Generation, Task Task)? _refresh;

    [ObservableProperty]
    public partial IReadOnlyList<Playlist> Playlists { get; private set; } = [];

    [ObservableProperty]
    public partial Playlist? Active { get; private set; }

    [ObservableProperty]
    public partial LoadStatus Status { get; private set; } = LoadStatus.Idle;

    [ObservableProperty]
    public partial CatalogIndex? Index { get; private set; }

    [ObservableProperty]
    public partial AccountInfo? Account { get; private set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; private set; }

    /// <summary>Raised before the active playlist changes or is deleted, so playback can stop and save first.</summary>
    public event EventHandler? ActivePlaylistChanging;

    /// <summary>A client for the active playlist when it is Xtream with a stored password.</summary>
    public XtreamClient? Client => Active is { } p ? source.XtreamClientFor(p) : null;

    public async Task InitializeAsync()
    {
        Playlists = playlists.Load();
        var stored = settings.Load().ActivePlaylist;
        Active = Playlists.FirstOrDefault(p => p.Id == stored) ?? FirstPlaylist();
        await LoadActiveAsync().ConfigureAwait(true);
    }

    public async Task ActivateAsync(Guid playlistId)
    {
        if (Active?.Id == playlistId || Playlists.FirstOrDefault(p => p.Id == playlistId) is not { } target)
        {
            return;
        }

        if (Active is not null)
        {
            ActivePlaylistChanging?.Invoke(this, EventArgs.Empty);
        }

        Active = target;
        settings.Save(settings.Load() with { ActivePlaylist = target.Id });
        await LoadActiveAsync().ConfigureAwait(true);
    }

    /// <summary>Refresh Playlist (Ctrl+R / F5), the toolbar button and "Try Again". Joins a refresh already running.</summary>
    public Task RefreshAsync()
    {
        if (Active is null)
        {
            return Task.CompletedTask;
        }

        if (_refresh is { } running && running.Generation == _generation && !running.Task.IsCompleted)
        {
            return running.Task;
        }

        var task = PerformRefreshAsync(_generation, Active);
        _refresh = (_generation, task);
        return task;
    }

    /// <summary>Validates Xtream logins against the server first; nothing is saved if that fails.</summary>
    /// <exception cref="RetuneException">Login rejected, server unreachable, or the password couldn't be stored.</exception>
    public async Task AddAsync(Playlist playlist, string? password)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        await VerifyAndStoreSecretAsync(playlist, password).ConfigureAwait(true);
        Playlists = [.. Playlists, playlist];
        playlists.Save(Playlists);
        await ActivateAsync(playlist.Id).ConfigureAwait(true);
    }

    /// <param name="newPassword">Null or empty keeps the stored password.</param>
    public async Task UpdateAsync(Playlist playlist, string? newPassword)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        var password = string.IsNullOrEmpty(newPassword) ? secrets.Find(playlist.Id) : newPassword;
        await VerifyAndStoreSecretAsync(playlist, password).ConfigureAwait(true);
        Playlists = [.. Playlists.Select(p => p.Id == playlist.Id ? playlist : p)];
        playlists.Save(Playlists);

        if (Active?.Id == playlist.Id)
        {
            Active = playlist;
            _generation++; // supersede any refresh still using the old details
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    public async Task DeleteAsync(Guid playlistId)
    {
        var wasActive = Active?.Id == playlistId;
        if (wasActive)
        {
            ActivePlaylistChanging?.Invoke(this, EventArgs.Empty);
            _generation++;
        }

        Playlists = [.. Playlists.Where(p => p.Id != playlistId)];
        playlists.Save(Playlists);
        secrets.Remove(playlistId);
        _ = cache.DeleteAsync(playlistId);
        library.Update(s => Core.Library.LibraryOperations.RemoveAll(s, playlistId));
        library.Flush();

        if (wasActive)
        {
            Active = FirstPlaylist();
            settings.Save(settings.Load() with { ActivePlaylist = Active?.Id });
            await LoadActiveAsync().ConfigureAwait(true);
        }
    }

    private Playlist? FirstPlaylist() => Playlists.Count > 0 ? Playlists[0] : null;

    private async Task VerifyAndStoreSecretAsync(Playlist playlist, string? password)
    {
        if (playlist.Source is not XtreamSource xtream)
        {
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            throw RetuneException.MissingPassword();
        }

        await source.ClientFor(new XtreamCredentials(xtream.Server, xtream.Username, password)).AuthenticateAsync().ConfigureAwait(true);
        secrets.Store(playlist.Id, password);
    }

    private async Task LoadActiveAsync()
    {
        var generation = ++_generation;
        IsRefreshing = false;
        Index = null;
        Account = null;

        if (Active is not { } playlist)
        {
            Status = LoadStatus.Idle;
            return;
        }

        // Nothing in an Xtream cache can play without the password, so don't show it.
        if (playlist.Source is XtreamSource && secrets.Find(playlist.Id) is null)
        {
            Status = LoadStatus.Failed(RetuneException.MissingPassword().Message);
            return;
        }

        Status = LoadStatus.Loading($"Opening {playlist.Name}…");
        var cached = await cache.LoadAsync(playlist.Id).ConfigureAwait(true);
        if (generation != _generation)
        {
            return;
        }

        if (cached is not null)
        {
            var index = await CatalogIndex.BuildAsync(cached).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            Index = index;
            Status = LoadStatus.Ready;
            if (cached.IsStale(time.GetUtcNow(), Catalog.MaxAge))
            {
                _ = RefreshAsync();
            }
        }
        else
        {
            await RefreshAsync().ConfigureAwait(true);
        }

        await LoadAccountAsync(generation, playlist).ConfigureAwait(true);
    }

    private async Task PerformRefreshAsync(int generation, Playlist playlist)
    {
        IsRefreshing = true;
        if (Index is null)
        {
            Status = LoadStatus.Loading(DownloadingMessage);
        }

        try
        {
            var catalog = await source.FetchAsync(playlist).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            _ = cache.SaveAsync(playlist.Id, catalog);
            var index = await CatalogIndex.BuildAsync(catalog).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            Index = index;
            Status = LoadStatus.Ready;
        }
        catch (RetuneException ex) when (generation == _generation)
        {
            // A cached catalog stays visible when a background refresh fails.
            if (Index is null)
            {
                Status = LoadStatus.Failed(ex.Message);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsRefreshing = false;
            }
        }
    }

    private async Task LoadAccountAsync(int generation, Playlist playlist)
    {
        if (source.XtreamClientFor(playlist) is not { } client)
        {
            return;
        }

        try
        {
            var account = await client.AuthenticateAsync().ConfigureAwait(true);
            if (generation == _generation)
            {
                Account = account;
            }
        }
        catch (RetuneException)
        {
            // The badge is optional; a failed check never blocks browsing.
        }
    }
}
