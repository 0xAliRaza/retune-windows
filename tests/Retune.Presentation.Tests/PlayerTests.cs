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
        _player = new PlayerViewModel(_engine, _session, _app.Controller, _app.SettingsService, _awake, _app.Time, a => a());
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
    public void Opening_the_player_never_unmutes()
    {
        var engine = new FakeEngine();
        engine.SetMuted(true);
        _app.Settings.Save(AppSettings.Default with { Volume = 80 });

        using var session = new PlaybackSession(engine, new LibraryPlaybackObserver(_app.Library, _app.Controller), _app.Time, a => a());
        using var player = new PlayerViewModel(engine, session, _app.Controller, _app.SettingsService, _awake, _app.Time, a => a());

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
