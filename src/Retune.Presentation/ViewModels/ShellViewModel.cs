using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Retune.Core.Browse;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Core.Xtream;
using Retune.Presentation.Services;

namespace Retune.Presentation.ViewModels;

/// <summary>A row in the sidebar's library list.</summary>
public sealed record SectionItem(LibrarySection Section, string Title, int Count, bool IsSelected);

/// <summary>A row in the sidebar's playlist list.</summary>
public sealed record PlaylistItem(Playlist Playlist, bool IsActive)
{
    public string Name => Playlist.Name;

    public string KindLabel => Playlist.KindLabel;
}

/// <summary>
/// The main browse window: sections, playlists, categories, content, search, sort, layout and the detail
/// back stack. Composes Core's browse rules; owns no IO. UI thread only.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);

    private readonly LibraryService _library;
    private readonly SettingsService _settings;
    private readonly IDialogs _dialogs;
    private readonly IPlayerLauncher _player;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly Func<SettingsViewModel> _createSettings;
    private readonly Stack<DetailViewModel> _details = new();
    private readonly HashSet<Guid> _defaultFilterApplied = [];
    private ITimer? _searchTimer;
    private string _appliedSearch = string.Empty;
    private (int Hidden, int Locked, int Pinned, int Unlocked, int Favorites, int Progress, int Recent) _libraryShape;

    public ShellViewModel(PlaylistController playlists, LibraryService library, SettingsService settings, IDialogs dialogs, IPlayerLauncher player, TimeProvider time, Action<Action> post, Func<SettingsViewModel> createSettings)
    {
        Playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogs = dialogs;
        _player = player;
        _time = time;
        _post = post;
        _createSettings = createSettings ?? throw new ArgumentNullException(nameof(createSettings));
        Content.Layout = Settings.LayoutFor(Section);
        Content.TileSize = Settings.TileSize;
        _libraryShape = Shape(library.State);

        Playlists.PropertyChanged += OnPlaylistsChanged;
        _library.Changed += OnLibraryChanged;
        _settings.Changed += OnSettingsChanged;
        _player.PlayingItemChanged += OnPlayingChanged;
    }

    public PlaylistController Playlists { get; }

    public ContentViewModel Content { get; } = new();

    public CategoryListViewModel Categories { get; } = new();

    public AppSettings Settings => _settings.Current;

    /// <summary>App-wide zoom (1 = 100%), applied by the window to the browse UI, dialogs and player controls.</summary>
    public double Zoom => ZoomLevel.Clamped(_settings.Current.Zoom);

    [ObservableProperty]
    public partial LibrarySection Section { get; private set; } = LibrarySection.Live;

    [ObservableProperty]
    public partial CategoryFilter Filter { get; private set; } = CategoryFilter.All;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BrowseSort Sort { get; private set; } = BrowseSort.Provider;

    [ObservableProperty]
    public partial DetailViewModel? Detail { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<SectionItem> Sections { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PlaylistItem> PlaylistItems { get; private set; } = [];

    public bool HasPlaylists => Playlists.Playlists.Count > 0;

    public bool CanGoBack => Detail is not null || SettingsPage is not null;

    /// <summary>The Settings page (gear in the sidebar, Ctrl+,), shown in place of the categories and content.</summary>
    [ObservableProperty]
    public partial SettingsViewModel? SettingsPage { get; private set; }

    public bool IsSettingsOpen => SettingsPage is not null;

    public IReadOnlyList<BrowseSort> SortOptions => Section == LibrarySection.Live
        ? [BrowseSort.Provider, BrowseSort.Title]
        : [BrowseSort.Provider, BrowseSort.Title, BrowseSort.RecentlyAdded, BrowseSort.Rating];

    public string SearchPlaceholder => $"Search {Section.Title()}";

    public bool HasUnlocks => _library.HasUnlocks;

    private BrowseContext? Context => Playlists is { Active: { } active, Index: { } index }
        ? new BrowseContext(active.Id, index, _library.State, _library.UnlockedCategories)
        : null;

    public void Dispose()
    {
        _searchTimer?.Dispose();
        CloseSettings();
        Playlists.PropertyChanged -= OnPlaylistsChanged;
        _library.Changed -= OnLibraryChanged;
        _settings.Changed -= OnSettingsChanged;
        _player.PlayingItemChanged -= OnPlayingChanged;
    }

    // ---- navigation ----------------------------------------------------------------------------

    [RelayCommand]
    public void SelectSection(LibrarySection section)
    {
        if (section == LibrarySection.Series && Playlists.Active?.SupportsSeries != true)
        {
            return;
        }

        CloseSettings();
        UnlockError = null;
        Section = section;
        _details.Clear();
        Detail = null;
        _appliedSearch = string.Empty;
        SearchText = string.Empty;
        Sort = BrowseSort.Provider;
        Content.Layout = Settings.LayoutFor(section);
        Filter = Context is { } c ? BrowseModel.DefaultFilter(c, section) : CategoryFilter.All;
        OnPropertyChanged(nameof(SortOptions));
        OnPropertyChanged(nameof(SearchPlaceholder));
        Recompute();
    }

    [RelayCommand]
    public void SelectCategory(CategoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        CloseSettings();
        UnlockError = null;
        Filter = row.Filter;
        _details.Clear();
        Detail = null;
        Recompute();
    }

    [RelayCommand]
    public void Back()
    {
        if (SettingsPage is not null)
        {
            CloseSettings();
            return;
        }

        if (_details.Count == 0)
        {
            return;
        }

        _details.Pop();
        Detail = _details.Count > 0 ? _details.Peek() : null;
    }

    /// <summary>The primary action: channels and Continue Watching play; movies and series open their page.</summary>
    [RelayCommand]
    public void Open(BrowseItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item.Source)
        {
            case Movie movie:
                Push(new MovieDetailViewModel(movie, Playlists.Active!.Id, Playlists.Client, _library, PlayMovie));
                break;
            case Series series:
                Push(new SeriesDetailViewModel(series, Playlists.Active!.Id, Playlists.Client, _library, PlayEpisode));
                break;
            default:
                Play(item);
                break;
        }
    }

    /// <summary>Plays directly (channels, the movie poster's play button, Continue Watching).</summary>
    [RelayCommand]
    public void Play(BrowseItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var items = PlayableFactory();
        var entries = Content.Entries;
        var index = IndexOf(entries, item);
        if (index < 0 || items is null)
        {
            return;
        }

        Lineup? lineup = item.Entry is not null
            ? Lineup.Build(entries.OfType<KeyedProgress>().ToList(), index, e => items.Restore(e.Progress.Item, id => Playlists.Index?.Movies.Item(id)), "Continue Watching")
            : item.Source switch
            {
                Channel => Lineup.Build(entries.OfType<Channel>().ToList(), index, items.Channel, Content.Title),
                Movie => Lineup.Build(entries.OfType<Movie>().ToList(), index, items.Movie, Content.Title),
                _ => null,
            };
        if (lineup is not null)
        {
            _player.Play(lineup);
        }
    }

    // ---- library actions -----------------------------------------------------------------------

    [RelayCommand]
    public void ToggleFavorite(BrowseItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.CanFavorite)
        {
            _library.Update(s => s.ToggleFavorite(item.LibraryKey));
        }
    }

    [RelayCommand]
    public void ToggleWatched(BrowseItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Source is not Movie movie)
        {
            return;
        }

        if (item.IsWatched)
        {
            _library.Update(s => s.MarkUnwatched(item.LibraryKey));
        }
        else if (PlayableFactory()?.Movie(movie) is { } playable && Locators.CredentialFree(playable, Playlists.Client is not null) is { } stored)
        {
            var duration = _library.State.ProgressFor(item.LibraryKey)?.Duration;
            _library.Update(s => s.MarkWatched(item.LibraryKey, stored, duration, _time.GetUtcNow()));
        }
    }

    [RelayCommand]
    public void RemoveFromRecent(BrowseItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _library.Update(s => s.RemoveRecentChannel(item.LibraryKey));
    }

    [RelayCommand]
    public void RemoveFromContinueWatching(BrowseItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _library.Update(s => s.RemoveFromContinueWatching(item.LibraryKey));
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        if (Playlists.Active is not { } active)
        {
            return;
        }

        var live = Section == LibrarySection.Live;
        var title = live ? "Clear Recently Watched?" : "Clear Continue Watching?";
        if (await _dialogs.ConfirmAsync(title, "This only affects this playlist. Favorites and watched marks are kept.", "Clear", destructive: true).ConfigureAwait(true))
        {
            _library.Update(s => live ? s.ClearRecentChannels(active.Id) : s.ClearContinueWatching(active.Id));
        }
    }

    // ---- categories ----------------------------------------------------------------------------

    [RelayCommand]
    public void TogglePin(CategoryRow row) => UpdateCategory(row, (s, key) => s.TogglePinned(key));

    [RelayCommand]
    public void Hide(CategoryRow row)
    {
        UpdateCategory(row, (s, key) => s.ToggleHidden(key));
        if (Filter is CategoryIdFilter f && f.Id == row?.CategoryId)
        {
            Filter = CategoryFilter.All;
            Recompute();
        }
    }

    /// <summary>Locks at once when a PIN exists; otherwise asks to create one first.</summary>
    [RelayCommand]
    public async Task LockAsync(CategoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_library.State.PinHash is null && !await _dialogs.CreatePinAsync().ConfigureAwait(true))
        {
            return;
        }

        UpdateCategory(row, (s, key) => s.SetLocked(key, true));
    }

    [RelayCommand]
    public async Task RemoveLockAsync(CategoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (await _dialogs.VerifyPinAsync("Enter your PIN to remove this lock.").ConfigureAwait(true))
        {
            UpdateCategory(row, (s, key) => s.SetLocked(key, false));
        }
    }

    /// <summary>Why the last unlock attempt was refused (wrong PIN, or the lockout); cleared on success or a new category.</summary>
    [ObservableProperty]
    public partial string? UnlockError { get; private set; }

    /// <summary>The locked-content prompt's Unlock button. Returns false for a wrong PIN or during lockout.</summary>
    public bool TryUnlock(string pin)
    {
        if (Content.LockedCategoryId is not { } id || Playlists.Active is not { } active)
        {
            return false;
        }

        if (_library.CheckPin(pin) == Core.Parental.PinCheck.Wrong)
        {
            UnlockError = _library.Pins.RejectionMessage;
            return false;
        }

        UnlockError = null;
        _library.Unlock(LibraryKeys.Category(active.Id, Section, id));
        return true;
    }

    [RelayCommand]
    public void LockAll() => _library.LockAll();

    // ---- view options --------------------------------------------------------------------------

    [RelayCommand]
    public void SetLayout(LayoutMode layout)
    {
        Content.Layout = layout;
        _settings.Update(s => s.WithLayout(Section, layout));
    }

    [RelayCommand]
    public void ZoomIn() => SetZoom(ZoomLevel.In(Zoom));

    [RelayCommand]
    public void ZoomOut() => SetZoom(ZoomLevel.Out(Zoom));

    [RelayCommand]
    public void ResetZoom() => SetZoom(ZoomLevel.Default);

    public void SetZoom(double zoom) => _settings.Update(s => s with { Zoom = ZoomLevel.Clamped(zoom) });

    [RelayCommand]
    public void SetTileSize(TileSize size)
    {
        Content.TileSize = size;
        _settings.Update(s => s with { TileSize = size });
    }

    [RelayCommand]
    public void SetSort(BrowseSort sort)
    {
        Sort = sort;
        Recompute();
    }

    // ---- playlists -----------------------------------------------------------------------------

    [RelayCommand]
    public Task RefreshAsync() => Playlists.RefreshAsync();

    [RelayCommand]
    public Task AddPlaylistAsync() => _dialogs.ShowPlaylistEditorAsync(null);

    [RelayCommand]
    public Task EditPlaylistAsync(Playlist playlist) => _dialogs.ShowPlaylistEditorAsync(playlist);

    [RelayCommand]
    public Task SelectPlaylistAsync(Playlist playlist) => Playlists.ActivateAsync(playlist?.Id ?? Guid.Empty);

    [RelayCommand]
    public async Task DeletePlaylistAsync(Playlist playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        var message = $"“{playlist.Name}” and its favorites, history and locks will be removed from this PC. This can't be undone.";
        if (await _dialogs.ConfirmAsync("Delete this playlist?", message, "Delete", destructive: true).ConfigureAwait(true))
        {
            await Playlists.DeleteAsync(playlist.Id).ConfigureAwait(true);
        }
    }

    // ---- recompute -----------------------------------------------------------------------------

    /// <summary>Rebuilds categories and content from the current state.</summary>
    public void Recompute()
    {
        OnPropertyChanged(nameof(HasPlaylists));
        OnPropertyChanged(nameof(HasUnlocks));
        Sections = BuildSections();
        PlaylistItems = [.. Playlists.Playlists.Select(p => new PlaylistItem(p, p.Id == Playlists.Active?.Id))];
        var context = Context;
        Categories.Show(context, Section, Filter);

        if (context is null)
        {
            ShowLoadState();
            return;
        }

        var content = BrowseModel.Content(context, Section, Filter, _appliedSearch, Sort);
        Content.Show(context.PlaylistId, BrowseModel.Title(context, Section, Filter, _appliedSearch), content, context.Library, _player.PlayingItemId, EmptyText());
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer?.Dispose();
        _searchTimer = _time.CreateTimer(_ => _post(ApplySearch), null, SearchDebounce, Timeout.InfiniteTimeSpan);
    }

    partial void OnDetailChanged(DetailViewModel? value) => OnPropertyChanged(nameof(CanGoBack));

    private void OnSettingsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Zoom));

    partial void OnSettingsPageChanged(SettingsViewModel? value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsSettingsOpen));
    }

    [RelayCommand]
    public void OpenSettings() => SettingsPage ??= _createSettings();

    [RelayCommand]
    public void CloseSettings()
    {
        SettingsPage?.Dispose();
        SettingsPage = null;
    }

    private void ApplySearch()
    {
        if (_appliedSearch == SearchText)
        {
            return;
        }

        _appliedSearch = SearchText;
        if (BrowseModel.IsSearching(SearchText))
        {
            _details.Clear();
            Detail = null;
        }

        Recompute();
    }

    private void ShowLoadState()
    {
        switch (Playlists.Status)
        {
            case LoadingStatus loading:
                Content.ShowStatus(ContentState.Loading, loading.Message, "Large playlists can take a minute the first time. After that they open instantly.");
                break;
            case FailedStatus failed:
                Content.ShowStatus(ContentState.Failed, "Couldn't load playlist", failed.Message);
                break;
            default:
                Content.ShowStatus(ContentState.NoPlaylist, "No playlist selected");
                break;
        }
    }

    private (string, string?) EmptyText() => (Filter, BrowseModel.IsSearching(_appliedSearch)) switch
    {
        (_, true) => ($"No results for “{_appliedSearch.Trim()}”", "Check the spelling or try a shorter search."),
        (ContinueWatchingFilter, _) => ("Nothing in progress", "Movies and episodes you start will appear here so you can pick up where you left off."),
        (FavoritesFilter, _) => ("No favorites yet", "Right-click anything or use the star to add it here."),
        _ => ("Nothing here", null),
    };

    private List<SectionItem> BuildSections()
    {
        var catalog = Playlists.Index?.Catalog;
        var sections = new List<SectionItem>
        {
            new(LibrarySection.Live, LibrarySection.Live.Title(), catalog?.Channels.Count ?? 0, Section == LibrarySection.Live),
            new(LibrarySection.Movies, LibrarySection.Movies.Title(), catalog?.Movies.Count ?? 0, Section == LibrarySection.Movies),
        };
        if (Playlists.Active?.SupportsSeries != false)
        {
            sections.Add(new(LibrarySection.Series, LibrarySection.Series.Title(), catalog?.Series.Count ?? 0, Section == LibrarySection.Series));
        }

        return sections;
    }

    private void Push(DetailViewModel detail)
    {
        _details.Push(detail);
        Detail = detail;
    }

    private PlaybackItems? PlayableFactory()
    {
        if (Playlists.Index is not { } index)
        {
            return null;
        }

        var names = index.Catalog.LiveCategories.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var extension = LiveFormats.Extension(Settings.LiveFormat, Playlists.Account?.Formats ?? []);
        return new PlaybackItems(Playlists.Client?.Urls, extension, id => id is not null && names.TryGetValue(id, out var n) ? n : null);
    }

    private void PlayMovie(Movie movie, bool resume)
    {
        if (PlayableFactory() is not { } items)
        {
            return;
        }

        var visible = Content.Entries.OfType<Movie>().ToList();
        var index = visible.FindIndex(m => m.Id == movie.Id);
        var lineup = index >= 0
            ? Lineup.Build(visible, index, items.Movie, Content.Title)
            : Lineup.Build([movie], 0, items.Movie, movie.Name);
        if (lineup is not null)
        {
            _player.Play(lineup, resume);
        }
    }

    private void PlayEpisode(Series series, SeriesDetail detail, Episode episode, bool resume)
    {
        if (PlayableFactory() is not { } items)
        {
            return;
        }

        var episodes = detail.EpisodesInOrder.ToList();
        var index = episodes.FindIndex(e => e.Id == episode.Id);
        if (Lineup.Build(episodes, index, e => items.Episode(series, e), series.Name) is { } lineup)
        {
            _player.Play(lineup, resume);
        }
    }

    private void UpdateCategory(CategoryRow? row, Func<LibraryState, string, LibraryState> change)
    {
        if (row?.CategoryId is { } id && Playlists.Active is { } active)
        {
            var key = LibraryKeys.Category(active.Id, Section, id);
            _library.Update(s => change(s, key));
        }
    }

    private void OnPlaylistsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlaylistController.Active):
                if (Section == LibrarySection.Series && Playlists.Active?.SupportsSeries == false)
                {
                    Section = LibrarySection.Live;
                }

                _details.Clear();
                Detail = null;
                Recompute();
                break;
            case nameof(PlaylistController.Index) when Playlists is { Index: not null, Active: { } active }:
                if (_defaultFilterApplied.Add(active.Id) && Context is { } context)
                {
                    Filter = BrowseModel.DefaultFilter(context, Section);
                }

                Recompute();
                break;
            case nameof(PlaylistController.Index) or nameof(PlaylistController.Status) or nameof(PlaylistController.Playlists):
                Recompute();
                break;
            default:
                break;
        }
    }

    /// <summary>Structural changes (what's listed) rebuild; flag changes (favourite, progress) refresh in place.</summary>
    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        var shape = Shape(_library.State);
        var structural = shape.Hidden != _libraryShape.Hidden || shape.Locked != _libraryShape.Locked || shape.Pinned != _libraryShape.Pinned
            || shape.Unlocked != _libraryShape.Unlocked
            || (Filter is FavoritesFilter && shape.Favorites != _libraryShape.Favorites)
            || (Filter is ContinueWatchingFilter && shape.Progress != _libraryShape.Progress)
            || (Filter is RecentFilter && shape.Recent != _libraryShape.Recent);
        _libraryShape = shape;

        if (structural)
        {
            Recompute();
        }
        else
        {
            Content.Refresh(_library.State, _player.PlayingItemId);
            OnPropertyChanged(nameof(HasUnlocks));
        }

        switch (Detail)
        {
            case MovieDetailViewModel movie:
                movie.RefreshLibrary();
                break;
            case SeriesDetailViewModel series:
                series.RefreshLibrary();
                break;
            default:
                break;
        }
    }

    private (int Hidden, int Locked, int Pinned, int Unlocked, int Favorites, int Progress, int Recent) Shape(LibraryState s) =>
        (s.HiddenCategories.Count, s.LockedCategories.Count, s.PinnedCategories.Count, _library.UnlockedCategories.Count,
         s.Favorites.Count, s.Progress.Count, s.RecentChannels.Count);

    private void OnPlayingChanged(object? sender, EventArgs e) => Content.Refresh(_library.State, _player.PlayingItemId);

    private static int IndexOf(IReadOnlyList<object> entries, BrowseItemViewModel item)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if ((entries[i] is IBrowsable b && ReferenceEquals(b, item.Source)) || (entries[i] is KeyedProgress k && ReferenceEquals(k, item.Entry)))
            {
                return i;
            }
        }

        return -1;
    }
}
