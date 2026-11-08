using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Presentation.Services;

namespace Retune.Presentation.ViewModels;

/// <summary>A hidden category of the active playlist, with the button that shows it again.</summary>
public sealed record HiddenCategoryRow(LibrarySection Section, string Key, string Name);

/// <summary>A zoom choice for the Settings page.</summary>
public sealed record ZoomOption(double Zoom, string Label);

/// <summary>A live stream format choice.</summary>
public sealed record LiveFormatOption(LiveFormat Format, string Label);

/// <summary>
/// The Settings page (spec ui-player §6, PLAN decision 9): playback options, the parental PIN, hidden categories,
/// watch history and the artwork cache. UI thread only.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly LibraryService _library;
    private readonly PlaylistController _playlists;
    private readonly IDialogs _dialogs;
    private readonly Action _clearArtworkCache;

    public SettingsViewModel(SettingsService settings, LibraryService library, PlaylistController playlists, IDialogs dialogs, Action clearArtworkCache)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _clearArtworkCache = clearArtworkCache ?? throw new ArgumentNullException(nameof(clearArtworkCache));
        _settings.Changed += OnSettingsChanged;
        _library.Changed += OnLibraryChanged;
        _playlists.PropertyChanged += OnPlaylistsChanged;
        RebuildHidden();
    }

    /// <summary>The choices, with what Automatic picks for the active playlist in brackets.</summary>
    public IReadOnlyList<LiveFormatOption> LiveFormats =>
    [
        new(Core.Models.LiveFormat.Automatic, $"Automatic ({AutomaticChoice})"),
        new(Core.Models.LiveFormat.Ts, "MPEG-TS (lower latency)"),
        new(Core.Models.LiveFormat.Hls, "HLS (more resilient)"),
    ];

    /// <summary>
    /// What Automatic resolves to: for Xtream, MPEG-TS unless the account doesn't allow it; M3U playlists always use
    /// their own links.
    /// </summary>
    public string AutomaticChoice => _playlists.Active?.Source is XtreamSource
        ? Core.Xtream.LiveFormats.Extension(Core.Models.LiveFormat.Automatic, _playlists.Account?.Formats ?? []) == "ts" ? "MPEG-TS" : "HLS"
        : "the playlist's own links";

    public LiveFormatOption SelectedLiveFormat
    {
        get => LiveFormats.FirstOrDefault(o => o.Format == _settings.Current.LiveFormat) ?? LiveFormats[0];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _settings.Update(s => s with { LiveFormat = value.Format });
        }
    }

    /// <summary>The zoom choices, as "100%" etc.</summary>
    public static IReadOnlyList<ZoomOption> ZoomOptions { get; } = [.. ZoomLevel.Steps.Select(z => new ZoomOption(z, ZoomLevel.Label(z)))];

    public ZoomOption SelectedZoom
    {
        get => ZoomOptions.FirstOrDefault(o => Math.Abs(o.Zoom - _settings.Current.Zoom) < 0.001) ?? new ZoomOption(_settings.Current.Zoom, ZoomLevel.Label(_settings.Current.Zoom));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _settings.Update(s => s with { Zoom = ZoomLevel.Clamped(value.Zoom) });
        }
    }

    public bool AutoplayNext
    {
        get => _settings.Current.AutoplayNext;
        set => _settings.Update(s => s with { AutoplayNext = value });
    }

    public bool AllowUnverifiedHttps
    {
        get => _settings.Current.AllowUnverifiedHttps;
        set => _settings.Update(s => s with { AllowUnverifiedHttps = value });
    }

    public bool HasPin => _library.State.PinHash is not null;

    public bool HasActivePlaylist => _playlists.Active is not null;

    public string? ActivePlaylistName => _playlists.Active?.Name;

    [ObservableProperty]
    public partial IReadOnlyList<HiddenCategoryRow> HiddenCategories { get; private set; } = [];

    public bool HasHiddenCategories => HiddenCategories.Count > 0;

    /// <summary>The collapsed section's subtitle: how many, never which.</summary>
    public string HiddenSummary => HiddenCategories.Count switch
    {
        0 => "None",
        1 => "1 hidden — expand to show it again",
        var n => $"{n.ToString(System.Globalization.CultureInfo.CurrentCulture)} hidden — expand to show them again",
    };

    /// <summary>The cache button turns into a disabled "Cleared" for the rest of the page's life.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArtworkButtonLabel))]
    [NotifyCanExecuteChangedFor(nameof(ClearArtworkCommand))]
    public partial bool ArtworkCleared { get; private set; }

    public string ArtworkButtonLabel => ArtworkCleared ? "Cleared" : "Clear";

    [RelayCommand]
    public async Task SetPinAsync() => await _dialogs.CreatePinAsync().ConfigureAwait(true);

    [RelayCommand]
    public async Task ChangePinAsync()
    {
        if (await _dialogs.VerifyPinAsync("Enter your current PIN.").ConfigureAwait(true))
        {
            await _dialogs.CreatePinAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task RemovePinAsync()
    {
        if (await _dialogs.VerifyPinAsync("Enter your PIN. Removing it also removes every category lock.").ConfigureAwait(true))
        {
            _library.Update(s => s.RemovePin());
            _library.LockAll();
        }
    }

    [RelayCommand]
    public void ShowCategory(HiddenCategoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_library.State.HiddenCategories.Contains(row.Key))
        {
            _library.Update(s => s.ToggleHidden(row.Key));
        }
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        if (_playlists.Active is not { } active
            || !await _dialogs.ConfirmAsync("Clear watch history?", "Favorites and watched marks are kept.", "Clear History", destructive: true).ConfigureAwait(true))
        {
            return;
        }

        _library.Update(s => s.ClearContinueWatching(active.Id).ClearRecentChannels(active.Id));
    }

    [RelayCommand(CanExecute = nameof(CanClearArtwork))]
    public void ClearArtwork()
    {
        _clearArtworkCache();
        ArtworkCleared = true;
    }

    [RelayCommand]
    public Task ShowShortcutsAsync() => _dialogs.ShowShortcutsAsync();

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _library.Changed -= OnLibraryChanged;
        _playlists.PropertyChanged -= OnPlaylistsChanged;
    }

    private bool CanClearArtwork() => !ArtworkCleared;

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedLiveFormat));
        OnPropertyChanged(nameof(AutoplayNext));
        OnPropertyChanged(nameof(AllowUnverifiedHttps));
        OnPropertyChanged(nameof(SelectedZoom));
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasPin));
        RebuildHidden();
    }

    private void OnPlaylistsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaylistController.Account) or nameof(PlaylistController.Active))
        {
            OnPropertyChanged(nameof(LiveFormats));
            OnPropertyChanged(nameof(SelectedLiveFormat));
        }

        if (e.PropertyName is nameof(PlaylistController.Active) or nameof(PlaylistController.Index))
        {
            OnPropertyChanged(nameof(HasActivePlaylist));
            OnPropertyChanged(nameof(ActivePlaylistName));
            RebuildHidden();
        }
    }

    /// <summary>Every hidden category of the active playlist, in section order; unknown ids read "Category &lt;id&gt;".</summary>
    private void RebuildHidden()
    {
        var rows = new List<HiddenCategoryRow>();
        if (_playlists.Active is { } active)
        {
            var catalog = _playlists.Index?.Catalog;
            foreach (var section in (LibrarySection[])[LibrarySection.Live, LibrarySection.Movies, LibrarySection.Series])
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var category in catalog?.CategoriesFor(section) ?? [])
                {
                    names.TryAdd(category.Id, category.Name.Trim()); // providers repeat ids; the first name wins
                }

                rows.AddRange(LibraryKeys.CategoryIds(_library.State.HiddenCategories, active.Id, section)
                    .Select(id => new HiddenCategoryRow(section, LibraryKeys.Category(active.Id, section, id), names.GetValueOrDefault(id) is { Length: > 0 } name ? name : $"Category {id}"))
                    .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase));
            }
        }

        HiddenCategories = rows;
        OnPropertyChanged(nameof(HasHiddenCategories));
        OnPropertyChanged(nameof(HiddenSummary));
    }
}
