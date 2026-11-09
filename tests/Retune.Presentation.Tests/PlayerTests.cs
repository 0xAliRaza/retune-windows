using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Presentation.Player;
using Retune.Presentation.Tests.Support;

namespace Retune.Presentation.Tests;

public sealed class PlayerTests : IAsyncLifetime
{
    private AppHarness _app = null!;
    private FakeEngine _engine = null!;
    private PlaybackSession _session = null!;
    private PlayerViewModel _player = null!;
    private Playlist _playlist = null!;
    private readonly FakeKeepAwake _awake = new();

    public async ValueTask InitializeAsync()
    {
        _app = await AppHarness.StartAsync();
        _playlist = await _app.AddDemoAsync();
        _engine = new FakeEngine();
        _session = new PlaybackSession(_engine, new LibraryPlaybackObserver(_app.Library, _app.Controller), _app.Time, a => a());
        _player = new PlayerViewModel(_engine, _session, _app.Controller, _app.Library, _app.SettingsService, _awake, _app.Time, a => a(), _app.Log);
    }

    public async ValueTask DisposeAsync()
    {
        _player.Dispose();
        _session.Dispose();
        await _app.DisposeAsync();
    }

    private Lineup Channels() => new(
        [.. Enumerable.Range(1, 5).Select(n => new PlaybackItem($"c{n}", ContentKind.Live, $"Channel {n}", "News", $"{_app.Provider.BaseUrl}/live/demo/demo/{n}.ts", StreamId: $"c{n}", Number: n * 10))],
        0,
        "News");

    private Lineup Movie() => new([new PlaybackItem("m1", ContentKind.Movie, "Film", null, $"{_app.Provider.BaseUrl}/movie/demo/demo/5000.mp4")], 0, "Movies");

    private void FirstFrame(double duration = 0, double position = 0)
    {
        _engine.Raise(new EngineFileLoaded());
        if (duration > 0)
        {
            _engine.Raise(new EngineDuration(duration));
        }

        _engine.Raise(new EngineFirstFrame());
        _engine.Raise(new EngineBuffering(false));
        if (position > 0)
        {
            _engine.Raise(new EnginePosition(position));
        }
    }

    [Fact]
    public void Playing_presents_the_player_and_closing_saves_progress_without_secrets()
    {
        _player.Play(Movie());
        FirstFrame(duration: 6000, position: 1234);

        Assert.True(_player.IsPresented);
        Assert.Equal("m1", _player.PlayingItemId);
        Assert.True(_awake.Awake);

        _player.Close();

        Assert.False(_player.IsPresented);
        Assert.False(_awake.Awake);
        var progress = _app.Library.State.ProgressFor(LibraryKeys.Content(_playlist.Id, ContentKind.Movie, "m1"));
        Assert.NotNull(progress);
        Assert.Equal(1234, progress.Position);
        Assert.Equal("retune://movie/5000.mp4", progress.Item.Url);
    }

    [Fact]
    public void Live_channels_are_recorded_as_recently_watched()
    {
        _player.Play(Channels());

        Assert.Equal(["c1"], _app.Library.State.RecentChannelIds(_playlist.Id));
    }

    [Fact]
    public void The_star_adds_and_removes_the_playing_channel_from_favorites()
    {
        Assert.False(_player.CanFavorite);
        _player.Play(Channels());
        FirstFrame();
        var key = LibraryKeys.Content(_playlist.Id, ContentKind.Live, "c1");

        Assert.True(_player.CanFavorite);
        Assert.False(_player.IsFavorite);

        _player.ToggleFavorite();
        Assert.True(_app.Library.State.IsFavorite(key));
        Assert.True(_player.IsFavorite);
        Assert.Equal("Added to Favorites", _player.FlashText);

        _player.ToggleFavorite();
        Assert.False(_app.Library.State.IsFavorite(key));
        Assert.Equal("Removed from Favorites", _player.FlashText);
    }

    [Fact]
    public void The_star_follows_the_channel_and_changes_made_elsewhere()
    {
        _player.Play(Channels());
        FirstFrame();
        var changes = new List<string?>();
        _player.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _app.Library.Update(s => s.ToggleFavorite(LibraryKeys.Content(_playlist.Id, ContentKind.Live, "c2")));
        Assert.Contains(nameof(PlayerViewModel.IsFavorite), changes);
        Assert.False(_player.IsFavorite);

        _player.Next();
        Assert.True(_player.IsFavorite);
    }

    [Fact]
    public void A_one_channel_list_still_has_its_list_with_that_channel()
    {
        var only = Channels().Items[0];
        _player.Play(new Lineup([only], 0, "Favorites"));

        Assert.True(_player.HasList);
        _player.ToggleList();

        Assert.True(_player.ListVisible);
        Assert.Equal("Favorites", _player.State.LineupTitle);
        var row = Assert.Single(_player.ListRows);
        Assert.Same(only, row.Item);
        Assert.True(row.IsCurrent);
    }

    [Fact]
    public void A_single_movie_has_no_up_next_list()
    {
        _player.Play(Movie());

        Assert.False(_player.HasList);
        _player.ToggleList();
        Assert.False(_player.ListVisible);
    }

    [Fact]
    public void Watching_an_episode_the_star_favourites_its_series()
    {
        _player.Play(new Lineup([new PlaybackItem("e1", ContentKind.Episode, "Show", "S1 E1 · Pilot", $"{_app.Provider.BaseUrl}/series/demo/demo/1.mp4", ParentId: "s7")], 0, "Show"));

        _player.ToggleFavorite();

        Assert.True(_app.Library.State.IsFavorite(LibraryKeys.Content(_playlist.Id, ContentKind.Series, "s7")));
        Assert.True(_player.IsFavorite);
    }

    [Fact]
    public void Picture_in_picture_is_asked_of_the_window_and_hides_the_panels()
    {
        var requests = 0;
        _player.PictureInPictureToggleRequested += (_, _) => requests++;
        _player.Play(Channels());
        _player.ToggleList();
        _player.ToggleStats();

        _player.TogglePictureInPicture();
        Assert.Equal(1, requests);

        _player.IsPictureInPicture = true; // what the window does once it is small
        Assert.False(_player.ListVisible);
        Assert.False(_player.StatsVisible);
        Assert.False(_player.ShortcutsVisible);
    }

    [Fact]
    public void Picture_in_picture_on_switching_apps_can_be_turned_off_from_the_player()
    {
        Assert.True(_player.AutoPictureInPicture);

        _player.ToggleAutoPictureInPicture();

        Assert.False(_player.AutoPictureInPicture);
        Assert.False(_app.SettingsService.Current.AutoPictureInPicture);
        Assert.Equal("Picture in picture when switching apps: off", _player.FlashText);

        _app.SettingsService.Update(s => s with { AutoPictureInPicture = true });
        Assert.True(_player.AutoPictureInPicture);
    }

    [Fact]
    public void Controls_hide_after_three_quiet_seconds_but_stay_while_paused()
    {
        _player.Play(Movie());
        FirstFrame(duration: 100);

        _app.Time.Advance(TimeSpan.FromMilliseconds(2900));
        Assert.True(_player.ControlsVisible);
        _app.Time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(_player.ControlsVisible);

        _player.UserActivity();
        Assert.True(_player.ControlsVisible);

        _engine.Raise(new EnginePaused(true));
        _app.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(_player.ControlsVisible);
    }

    [Fact]
    public void Volume_keys_step_by_five_flash_and_persist()
    {
        _player.Play(Movie());
        _engine.SetVolume(97);

        _player.VolumeUp();

        Assert.Equal(100, _engine.Volume);
        Assert.Equal("Volume 100%", _player.FlashText);
        _app.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(_player.FlashText);
        Assert.Equal(100, _app.Settings.Saved.Volume);

        _player.VolumeDown();
        _app.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(95, _app.Settings.Saved.Volume);
    }

    [Fact]
    public void Esc_closes_the_shortcuts_panel_before_the_player_and_it_keeps_the_controls_up()
    {
        _player.Play(Channels());
        FirstFrame();
        _player.ToggleShortcuts();

        _app.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(_player.ControlsVisible);

        _player.Escape();
        Assert.False(_player.ShortcutsVisible);
        Assert.True(_player.IsPresented);
    }

    [Fact]
    public void Turning_autoplay_off_in_settings_reaches_the_running_player()
    {
        _app.SettingsService.Update(s => s with { AutoplayNext = false });

        Assert.False(_session.AutoplayEnabled);
    }

    [Fact]
    public void Certificates_are_verified_unless_the_user_allows_unverified_streams()
    {
        Assert.True(_engine.VerifiesCertificates);

        _app.SettingsService.Update(s => s with { AllowUnverifiedHttps = true });
        Assert.False(_engine.VerifiesCertificates);

        _app.SettingsService.Update(s => s with { AllowUnverifiedHttps = false });
        Assert.True(_engine.VerifiesCertificates);
    }

    [Fact]
    public void The_wheel_zaps_live_channels_and_shift_wheel_is_volume()
    {
        _player.Play(Channels());
        FirstFrame();
        _engine.SetVolume(50);

        _player.Wheel(up: false, volume: false);
        _player.Wheel(up: false, volume: false);
        Assert.Equal(2, _session.State.Index);
        Assert.Equal(50, _engine.Volume);

        _player.Wheel(up: true, volume: true);
        Assert.Equal(55, _engine.Volume);
        Assert.Equal(2, _session.State.Index);
    }

    [Fact]
    public void The_wheel_is_volume_for_films()
    {
        _player.Play(Movie());
        FirstFrame(duration: 600);
        _engine.SetVolume(50);

        _player.Wheel(up: false, volume: false);

        Assert.Equal(45, _engine.Volume);
        Assert.Equal("Volume 45%", _player.FlashText);
    }

    [Fact]
    public void A_refused_forward_seek_says_why_and_is_logged_while_going_back_still_works()
    {
        _player.Play(Movie());
        FirstFrame(duration: 600, position: 100);
        _engine.CanSeekAhead = false;

        _player.SeekForward();
        Assert.Equal("This server doesn't allow skipping ahead that far yet", _player.FlashText);
        Assert.Contains(_app.Log.Entries, e => e.Area == "player");

        _player.SeekBack();
        Assert.Equal("−10s", _player.FlashText);
    }

    [Fact]
    public void A_video_of_unknown_length_says_it_cannot_be_skipped()
    {
        _player.Play(Movie());
        FirstFrame(duration: 0, position: 30);

        _player.SeekForward();

        Assert.Equal("This video can't be skipped (its length is unknown)", _player.FlashText);
    }

    [Fact]
    public void Opening_the_player_never_unmutes()
    {
        var engine = new FakeEngine();
        engine.SetMuted(true);
        _app.Settings.Save(AppSettings.Default with { Volume = 80 });

        using var session = new PlaybackSession(engine, new LibraryPlaybackObserver(_app.Library, _app.Controller), _app.Time, a => a());
        using var player = new PlayerViewModel(engine, session, _app.Controller, _app.Library, _app.SettingsService, _awake, _app.Time, a => a(), _app.Log);

        Assert.True(engine.Muted);
    }

    [Fact]
    public void Raising_the_volume_unmutes()
    {
        _player.Play(Movie());
        _engine.SetMuted(true);

        _player.VolumeUp();

        Assert.False(_engine.Muted);
    }

    [Fact]
    public void Player_and_browse_settings_never_overwrite_each_other()
    {
        _player.Play(Movie());
        _engine.SetVolume(50);

        _player.VolumeUp();
        _app.Shell.SetTileSize(TileSize.Small);
        _app.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(55, _app.Settings.Saved.Volume);
        Assert.Equal(TileSize.Small, _app.Settings.Saved.TileSize);
        Assert.Equal(_playlist.Id, _app.Settings.Saved.ActivePlaylist);
    }

    [Fact]
    public void Subtitle_toggle_remembers_the_last_track()
    {
        _player.Play(Movie());
        _player.ToggleSubtitles();
        Assert.Equal("No subtitles in this stream", _player.FlashText);

        _engine.SetTracks([new MediaTrack(1, TrackType.Subtitle, "eng", null, "srt", null, false), new MediaTrack(2, TrackType.Subtitle, "ger", null, "srt", null, true)]);
        _player.ToggleSubtitles();
        Assert.Null(_engine.SelectedSubtitle);
        Assert.Equal("Subtitles off", _player.FlashText);

        _engine.SetTracks([new MediaTrack(1, TrackType.Subtitle, "eng", null, "srt", null, false), new MediaTrack(2, TrackType.Subtitle, "ger", null, "srt", null, false)]);
        _player.ToggleSubtitles();
        Assert.Equal(2, _engine.SelectedSubtitle);
    }

    [Fact]
    public void Typing_a_channel_number_tunes_to_it()
    {
        _player.Play(Channels());

        _player.Digit(3);
        _player.Digit(0);
        Assert.Equal("30", _player.NumberEntry);
        _app.Time.Advance(PlayerViewModel.NumberEntryTimeout);

        Assert.Null(_player.NumberEntry);
        Assert.Equal("c3", _player.State.Item?.Id);

        _player.Digit(9);
        _player.CommitNumber();
        Assert.Equal("No channel 9 in this list", _player.FlashText);
    }

    [Fact]
    public void Speed_changes_on_demand_only()
    {
        _player.Play(Channels());
        _player.Faster();
        Assert.Equal(1, _engine.Speed);

        _player.Play(Movie());
        _player.Faster();
        Assert.Equal(1.25, _engine.Speed);
        Assert.Equal("Speed 1.25×", _player.FlashText);
        _player.NormalSpeed();
        Assert.Equal("Speed 1×", _player.FlashText);
    }

    [Fact]
    public void The_spinner_only_shows_for_buffering_longer_than_250ms()
    {
        _player.Play(Movie());
        FirstFrame(duration: 100);

        _engine.Raise(new EngineBuffering(true));
        Assert.False(_player.ShowsSpinner);
        _app.Time.Advance(TimeSpan.FromMilliseconds(250));
        Assert.True(_player.ShowsSpinner);
        _engine.Raise(new EngineBuffering(false));
        Assert.False(_player.ShowsSpinner);
    }

    [Fact]
    public void Escape_closes_the_list_then_the_stats_then_the_player()
    {
        _player.Play(Channels());
        _player.ToggleList();
        _player.ToggleStats();

        _player.Escape();
        Assert.False(_player.ListVisible);
        Assert.True(_player.StatsVisible);
        _player.Escape();
        Assert.False(_player.StatsVisible);
        Assert.True(_player.IsPresented);
        _player.Escape();
        Assert.False(_player.IsPresented);
    }

    [Fact]
    public void The_list_panel_filters_and_jumps()
    {
        _player.Play(Channels());
        _player.ToggleList();
        Assert.Equal(5, _player.ListRows.Count);
        Assert.True(_player.ListRows[0].IsCurrent);

        _player.ListFilter = "channel 4";
        var row = Assert.Single(_player.ListRows);
        _player.JumpTo(row);

        Assert.Equal("c4", _player.State.Item?.Id);
        Assert.False(_player.ListVisible);
    }

    [Fact]
    public async Task The_live_guide_shows_now_and_next_from_the_provider()
    {
        _player.Play(Channels());
        Assert.Null(_player.NowText); // not fetched while the channel is still tuning (zapping past it)
        FirstFrame();

        for (var i = 0; i < 100 && _player.NowText is null; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.StartsWith("Now: ", _player.NowText, StringComparison.Ordinal);
        Assert.StartsWith("Next: ", _player.NextText, StringComparison.Ordinal);
        Assert.Equal(_player.NowText, _player.Subtitle);
    }

    [Fact]
    public void Changing_playlist_stops_playback()
    {
        _player.Play(Movie());

        _player.StopForPlaylistChange();

        Assert.False(_player.IsPresented);
        Assert.Equal(PlaybackPhase.Idle, _player.State.Phase);
    }

    private sealed class FakeKeepAwake : IKeepAwake
    {
        public bool Awake { get; private set; }

        public void KeepAwake(bool awake) => Awake = awake;
    }

    private sealed class FakeEngine : IPlayerEngine
    {
        public event EventHandler<EngineEvent>? EventRaised;

        public double Volume { get; private set; } = 100;

        public bool Muted { get; private set; }

        public double Speed { get; private set; } = 1;

        public IReadOnlyList<MediaTrack> Tracks { get; private set; } = [];

        public long? SelectedSubtitle { get; private set; }

        public void Raise(EngineEvent e) => EventRaised?.Invoke(this, e);

        public void SetTracks(IReadOnlyList<MediaTrack> tracks)
        {
            Tracks = tracks;
            Raise(new EngineTracksChanged());
        }

        public void Load(string url, bool live, double? startSeconds)
        {
            if (live)
            {
                Speed = 1;
            }
        }

        public void StopPlayback()
        {
        }

        public void SetPaused(bool paused) => Raise(new EnginePaused(paused));

        public void SeekAbsolute(double seconds)
        {
        }

        public bool VerifiesCertificates { get; private set; } = true;

        public void SetVerifyCertificates(bool verify) => VerifiesCertificates = verify;

        public bool CanSeekAhead { get; set; } = true;

        public bool CanSeekTo(double seconds) => CanSeekAhead;

        public void SeekRelative(double seconds)
        {
        }

        public void SetVolume(double volume)
        {
            Volume = VolumeLevel.Clamped(volume);
            Raise(new EngineVolume(Volume, Muted));
        }

        public void SetMuted(bool muted) => Muted = muted;

        public void SetSpeed(double speed) => Speed = speed;

        public void SelectAudio(long id)
        {
        }

        public void SelectSubtitle(long? id) => SelectedSubtitle = id;

        public void ApplySubtitleStyle(SubtitleStyle style)
        {
        }

        public void SetSubtitleDelay(double seconds)
        {
        }

        public PlaybackStats ReadStats() => new(Width: 1920, Height: 1080, SourceFps: 25);

        public Task<bool> ScreenshotAsync(string path) => Task.FromResult(true);
    }
}
