using CommunityToolkit.Mvvm.ComponentModel;
using Retune.Core.Diagnostics;
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
    SettingsService settings,
    CatalogSource source,
    LibraryService library,
    TimeProvider time,
    IAppLog log) : ObservableObject, IDisposable
{
    public const string DownloadingMessage = "Downloading channels, movies and series…";

    private int _generation;
    private CancellationTokenSource _refreshCancel = new();
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
        var stored = settings.Current.ActivePlaylist;
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
        settings.Update(s => s with { ActivePlaylist = target.Id });
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
        if (string.IsNullOrEmpty(newPassword)
            && Playlists.FirstOrDefault(p => p.Id == playlist.Id)?.Source is XtreamSource before
            && playlist.Source is XtreamSource after
            && !Core.Policies.UrlPolicy.SameOrigin(before.Server, after.Server))
        {
            // Otherwise anyone at the open app could send the saved password to a server of their choosing.
            throw RetuneException.PasswordForNewServer();
        }

        var password = string.IsNullOrEmpty(newPassword) ? secrets.Find(playlist.Id) : newPassword;
        await VerifyAndStoreSecretAsync(playlist, password).ConfigureAwait(true);
        Playlists = [.. Playlists.Select(p => p.Id == playlist.Id ? playlist : p)];
        playlists.Save(Playlists);

        if (Active?.Id == playlist.Id)
        {
            Active = playlist;
            NextGeneration(); // supersede any refresh still using the old details
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    public async Task DeleteAsync(Guid playlistId)
    {
        var wasActive = Active?.Id == playlistId;
        if (wasActive)
        {
            ActivePlaylistChanging?.Invoke(this, EventArgs.Empty);
            NextGeneration();
        }

        Playlists = [.. Playlists.Where(p => p.Id != playlistId)];
        playlists.Save(Playlists);
        try
        {
            secrets.Remove(playlistId);
        }
        catch (RetuneException ex)
        {
            // The playlist is already gone from the list; finish removing it and report the leftover secret.
            log.Failure("playlist delete", "The saved password couldn't be removed from Credential Manager.", ex);
        }
        cache.DeleteAsync(playlistId).Forget(log, "catalog cache delete");
        library.Update(s => Core.Library.LibraryOperations.RemoveAll(s, playlistId));
        library.Flush();

        if (wasActive)
        {
            Active = FirstPlaylist();
            settings.Update(s => s with { ActivePlaylist = Active?.Id });
            await LoadActiveAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Stops any download still running (app exit).</summary>
    public void Dispose()
    {
        _refreshCancel.Cancel();
        _refreshCancel.Dispose();
    }

    /// <summary>Moves to a new load generation and cancels the download of the previous one (spec §4.1).</summary>
    private int NextGeneration()
    {
        _refreshCancel.Cancel();
        _refreshCancel.Dispose();
        _refreshCancel = new CancellationTokenSource();
        return ++_generation;
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
        var generation = NextGeneration();
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
                RefreshAsync().Forget(log, "background refresh");
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
            var catalog = await source.FetchAsync(playlist, _refreshCancel.Token).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            cache.SaveAsync(playlist.Id, catalog).Forget(log, "catalog cache save");
            var index = await CatalogIndex.BuildAsync(catalog).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            Index = index;
            Status = LoadStatus.Ready;
        }
        catch (RetuneException ex)
        {
            // A superseded refresh fails quietly; a current one keeps a cached catalog visible.
            if (generation == _generation && Index is null)
            {
                Status = LoadStatus.Failed(ex.Message);
            }
        }
        catch (OperationCanceledException) when (generation != _generation)
        {
            // Cancelled because the playlist changed: nothing to report.
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
