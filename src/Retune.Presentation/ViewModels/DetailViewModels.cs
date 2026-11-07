using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Retune.Core.Errors;
using Retune.Core.Formatting;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Core.Xtream;
using Retune.Presentation.Services;

namespace Retune.Presentation.ViewModels;

/// <summary>Shared hero metadata for both detail pages.</summary>
public abstract partial class DetailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial MediaInfo Info { get; protected set; } = MediaInfo.Empty;

    [ObservableProperty]
    public partial bool IsFavorite { get; protected set; }

    public abstract string Title { get; }

    public abstract Uri? Poster { get; }

    public Uri? Backdrop => Info.Backdrop ?? Poster;

    public string? Plot => Info.Plot;

    /// <summary>"★ 7.4 · 2021 · 1h 42m · Drama".</summary>
    public string Metadata => string.Join("  ·  ", new[]
    {
        Info.Rating is { } r ? $"★ {Format.Rating(r)}" : null,
        Info.Year,
        Info.DurationSeconds is { } d && d > 0 ? Format.Runtime(d) : null,
        Info.Genre,
    }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public Uri? Trailer => Info.YoutubeTrailer is { Length: > 0 } id ? new Uri($"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}") : null;

    partial void OnInfoChanged(MediaInfo value)
    {
        OnPropertyChanged(nameof(Backdrop));
        OnPropertyChanged(nameof(Plot));
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(Trailer));
    }
}

public sealed partial class MovieDetailViewModel : DetailViewModel
{
    private readonly LibraryService _library;
    private readonly string _key;
    private readonly Action<Movie, bool> _play;

    public MovieDetailViewModel(Movie movie, Guid playlistId, XtreamClient? client, LibraryService library, Action<Movie, bool> play)
    {
        ArgumentNullException.ThrowIfNull(movie);
        Movie = movie;
        _library = library;
        _play = play;
        _key = LibraryKeys.Content(playlistId, ContentKind.Movie, movie.Id);
        Info = new MediaInfo(Rating: movie.Rating, Poster: movie.Poster);
        RefreshLibrary();
        if (client is not null)
        {
            _ = LoadAsync(client);
        }
    }

    public Movie Movie { get; }

    public override string Title => Movie.Name;

    public override Uri? Poster => Info.Poster ?? Movie.Poster;

    [ObservableProperty]
    public partial string PrimaryLabel { get; private set; } = "Play";

    [ObservableProperty]
    public partial bool CanStartOver { get; private set; }

    public void RefreshLibrary()
    {
        IsFavorite = _library.State.IsFavorite(_key);
        var progress = _library.State.ProgressFor(_key);
        CanStartOver = progress?.IsResumable == true;
        PrimaryLabel = CanStartOver ? $"Resume · {Format.Remaining(progress!.Remaining)}" : "Play";
    }

    [RelayCommand]
    private void Play() => _play(Movie, true);

    [RelayCommand]
    private void StartOver() => _play(Movie, false);

    [RelayCommand]
    private void ToggleFavorite() => _library.Update(s => s.ToggleFavorite(_key));

    /// <summary>Details are optional: a failure just leaves the list data showing.</summary>
    private async Task LoadAsync(XtreamClient client)
    {
        try
        {
            var info = await client.MovieInfoAsync(Movie.Id).ConfigureAwait(true);
            Info = info with { Rating = info.Rating ?? Movie.Rating, Poster = info.Poster ?? Movie.Poster };
            OnPropertyChanged(nameof(Poster));
        }
        catch (RetuneException)
        {
            // Keep the catalog data.
        }
    }
}

/// <summary>One episode row on the series page.</summary>
public sealed partial class EpisodeItemViewModel(Episode episode, string key) : ObservableObject
{
    public Episode Episode { get; } = episode;

    public string Key { get; } = key;

    public string Title => $"{Episode.Number}. {Episode.Title}";

    public string? Runtime => Episode.DurationSeconds is { } d && d > 0 ? Format.Runtime(d) : null;

    [ObservableProperty]
    public partial bool IsWatched { get; private set; }

    [ObservableProperty]
    public partial double Progress { get; private set; }

    public void Refresh(LibraryState library)
    {
        var p = library.ProgressFor(Key);
        IsWatched = p?.IsFinished == true;
        Progress = p is { Fraction: > 0.01 } && !IsWatched ? p.Fraction : 0;
    }
}

public sealed partial class SeriesDetailViewModel : DetailViewModel
{
    private readonly Guid _playlistId;
    private readonly XtreamClient? _client;
    private readonly LibraryService _library;
    private readonly Action<Series, SeriesDetail, Episode, bool> _play;
    private readonly string _key;
    private SeriesDetail? _detail;
    private List<EpisodeItemViewModel> _all = [];

    public SeriesDetailViewModel(Series series, Guid playlistId, XtreamClient? client, LibraryService library, Action<Series, SeriesDetail, Episode, bool> play)
    {
        ArgumentNullException.ThrowIfNull(series);
        Series = series;
        _playlistId = playlistId;
        _client = client;
        _library = library;
        _play = play;
        _key = LibraryKeys.Content(playlistId, ContentKind.Series, series.Id);
        Info = new MediaInfo(Plot: series.Plot, Genre: series.Genre, ReleaseDate: series.ReleaseDate, Rating: series.Rating, Poster: series.Cover, Backdrop: series.Backdrop);
        IsFavorite = library.State.IsFavorite(_key);
        _ = LoadAsync();
    }

    public Series Series { get; }

    public override string Title => Series.Name;

    public override Uri? Poster => Series.Cover ?? Info.Poster;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; } = true;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<Season> Seasons { get; private set; } = [];

    [ObservableProperty]
    public partial Season? SelectedSeason { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<EpisodeItemViewModel> Episodes { get; private set; } = [];

    [ObservableProperty]
    public partial string? PrimaryLabel { get; private set; }

    private EpisodePick? Pick { get; set; }

    public void RefreshLibrary()
    {
        IsFavorite = _library.State.IsFavorite(_key);
        foreach (var e in _all)
        {
            e.Refresh(_library.State);
        }

        UpdatePrimary();
    }

    [RelayCommand]
    private void ToggleFavorite() => _library.Update(s => s.ToggleFavorite(_key));

    [RelayCommand]
    private void PlayPrimary()
    {
        if (Pick is { } pick && _detail is { } detail)
        {
            var episode = _all[pick.Index].Episode;
            _play(Series, detail, episode, pick.Action == EpisodeAction.Resume);
        }
    }

    [RelayCommand]
    private void PlayEpisode(EpisodeItemViewModel episode)
    {
        if (_detail is { } detail)
        {
            _play(Series, detail, episode.Episode, true);
        }
    }

    [RelayCommand]
    private void ToggleWatched(EpisodeItemViewModel episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        if (episode.IsWatched)
        {
            _library.Update(s => s.MarkUnwatched(episode.Key));
            return;
        }

        if (_client is null)
        {
            return;
        }

        var item = new PlaybackItem(episode.Episode.Id, ContentKind.Episode, Series.Name, $"S{episode.Episode.Season} E{episode.Episode.Number} · {episode.Episode.Title}", _client.Urls.Episode(episode.Episode), episode.Episode.Image ?? Series.Cover, ParentId: Series.Id);
        if (Locators.CredentialFree(item, hasAccount: true) is { } stored)
        {
            var duration = episode.Episode.DurationSeconds ?? _library.State.ProgressFor(episode.Key)?.Duration;
            _library.Update(s => s.MarkWatched(episode.Key, stored, duration, DateTimeOffset.UtcNow));
        }
    }

    [RelayCommand]
    private Task Retry() => LoadAsync();

    partial void OnSelectedSeasonChanged(Season? value) =>
        Episodes = value is null ? [] : _all.Where(e => e.Episode.Season == value.Number).ToList();

    private async Task LoadAsync()
    {
        if (_client is null)
        {
            IsLoading = false;
            Error = RetuneException.UnreadableResponse().Message;
            return;
        }

        IsLoading = true;
        Error = null;
        try
        {
            var detail = await _client.SeriesInfoAsync(Series.Id).ConfigureAwait(true);
            _detail = detail;
            Info = detail.Info with
            {
                Plot = detail.Info.Plot ?? Series.Plot,
                Rating = detail.Info.Rating ?? Series.Rating,
                Backdrop = detail.Info.Backdrop ?? Series.Backdrop,
                Poster = detail.Info.Poster ?? Series.Cover,
            };
            _all = detail.EpisodesInOrder.Select(e => new EpisodeItemViewModel(e, LibraryKeys.Content(_playlistId, ContentKind.Episode, e.Id))).ToList();
            Seasons = detail.Seasons;
            RefreshLibrary();
            SelectedSeason = Pick is { } pick
                ? detail.Seasons.FirstOrDefault(s => s.Number == _all[pick.Index].Episode.Season) ?? First(detail.Seasons)
                : First(detail.Seasons);
        }
        catch (RetuneException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static Season? First(IReadOnlyList<Season> seasons) => seasons.Count > 0 ? seasons[0] : null;

    private void UpdatePrimary()
    {
        Pick = EpisodePick.Choose(_all.Count, i => _library.State.ProgressFor(_all[i].Key));
        PrimaryLabel = Pick is { } pick ? pick.Label(_all[pick.Index].Episode, _all.Any(e => e.IsWatched)) : null;
    }
}
