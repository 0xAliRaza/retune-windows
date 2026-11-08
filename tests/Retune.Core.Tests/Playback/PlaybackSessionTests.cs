using Microsoft.Extensions.Time.Testing;
using Retune.Core.Models;
using Retune.Core.Playback;

namespace Retune.Core.Tests.Playback;

public sealed class PlaybackSessionTests : IDisposable
{
    private readonly FakeEngine _engine = new();
    private readonly FakeObserver _observer = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    private readonly PlaybackSession _session;

    public PlaybackSessionTests()
    {
        _session = new PlaybackSession(_engine, _observer, _time, action => action());
    }

    public void Dispose() => _session.Dispose();

    private static PlaybackItem Live(int n) => new($"c{n}", ContentKind.Live, $"Channel {n}", null, $"http://h/live/{n}.ts", StreamId: $"c{n}");

    private static PlaybackItem Movie(string id) => new(id, ContentKind.Movie, id, null, $"http://h/movie/{id}.mp4");

    private static PlaybackItem Episode(int n, string series = "s") => new($"e{n}", ContentKind.Episode, "Show", $"S1 E{n}", $"http://h/series/e{n}.mp4", ParentId: series);

    private static Lineup Channels(int start = 0) => new([Live(0), Live(1), Live(2), Live(3)], start, "News");

    /// <summary>Simulates the file opening and showing its first frame.</summary>
    private void Begin(double duration = 0, double position = 0)
    {
        _engine.Raise(new EngineFileLoaded());
        if (duration > 0)
        {
            _engine.Raise(new EngineDuration(duration));
        }

        _engine.Raise(new EngineFirstFrame());
        if (position > 0)
        {
            _engine.Raise(new EnginePosition(position));
        }
    }

    [Fact]
    public void Starting_a_live_lineup_loads_at_once_and_records_the_channel()
    {
        _session.Start(Channels(1));

        Assert.Equal(["http://h/live/1.ts"], _engine.Loads.Select(l => l.Url));
        Assert.True(_engine.Loads[0].Live);
        Assert.Null(_engine.Loads[0].Start);
        Assert.Equal(PlaybackPhase.Tuning, _session.State.Phase);
        Assert.Equal(["c1"], _observer.LiveStarts.Select(i => i.Id));

        Begin();
        Assert.Equal(PlaybackPhase.Playing, _session.State.Phase);
        Assert.True(_session.State.HasVideoFrame);
    }

    [Fact]
    public void Zapping_stops_at_once_but_connects_only_after_350ms_of_quiet()
    {
        _session.Start(Channels());
        Begin();

        _session.Step(+1);
        Assert.Equal(1, _engine.Stops);
        Assert.Equal("c1", _session.State.Item?.Id);
        Assert.Equal(PlaybackPhase.Tuning, _session.State.Phase);

        _time.Advance(TimeSpan.FromMilliseconds(200));
        _session.Step(+1);
        _time.Advance(TimeSpan.FromMilliseconds(349));
        Assert.Single(_engine.Loads);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["http://h/live/0.ts", "http://h/live/2.ts"], _engine.Loads.Select(l => l.Url));
        Assert.Equal(["c0", "c2"], _observer.LiveStarts.Select(i => i.Id));
    }

    [Fact]
    public void Live_lineups_wrap_and_on_demand_lineups_stop_at_the_ends()
    {
        _session.Start(Channels());
        _session.Step(-1);
        Assert.Equal("c3", _session.State.Item?.Id);

        _session.Start(new Lineup([Movie("a"), Movie("b")], 0, "Movies"));
        _session.Step(-1);
        Assert.Equal("a", _session.State.Item?.Id);
        Assert.False(_session.State.CanGoPrevious);
        Assert.True(_session.State.CanGoNext);
    }

    [Fact]
    public void Last_channel_flips_between_the_two_most_recent()
    {
        _session.Start(Channels());
        _session.JumpTo(3);
        _session.LastChannel();
        Assert.Equal("c0", _session.State.Item?.Id);
        _session.LastChannel();
        Assert.Equal("c3", _session.State.Item?.Id);

        _session.Start(Channels(2));
        _session.LastChannel();
        Assert.Equal("c2", _session.State.Item?.Id);
    }

    [Fact]
    public void Failures_reconnect_after_1_2_4_6_seconds_then_give_up()
    {
        _session.Start(Channels());
        Begin();

        TimeSpan[] delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)];
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            _engine.Raise(new EngineFailed("connection refused"));
            Assert.Equal(PlaybackPhase.Reconnecting, _session.State.Phase);
            Assert.Equal(attempt, _session.State.ReconnectAttempt);

            var loads = _engine.Loads.Count;
            _time.Advance(delays[attempt - 1] - TimeSpan.FromMilliseconds(1));
            Assert.Equal(loads, _engine.Loads.Count);
            _time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(loads + 1, _engine.Loads.Count);
        }

        _engine.Raise(new EngineFailed("connection refused"));

        Assert.Equal(PlaybackPhase.Failed, _session.State.Phase);
        Assert.Contains("connection refused", _session.State.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("connection limit", _session.State.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rejected_certificate_fails_at_once_without_reconnecting()
    {
        _session.Start(Channels());
        var loads = _engine.Loads.Count;

        _engine.Raise(new EngineCertificateRejected());
        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(PlaybackPhase.Failed, _session.State.Phase);
        Assert.Equal(PlaybackSession.CertificateRejectedMessage, _session.State.ErrorMessage);
        Assert.Equal(loads, _engine.Loads.Count);
    }

    [Fact]
    public void A_frame_resets_the_reconnect_counter_and_try_again_starts_fresh()
    {
        _session.Start(Channels());
        _engine.Raise(new EngineFailed("x"));
        _time.Advance(TimeSpan.FromSeconds(1));
        Begin();
        Assert.Equal(0, _session.State.ReconnectAttempt);

        for (var i = 0; i < 5; i++)
        {
            _engine.Raise(new EngineFailed("x"));
            _time.Advance(TimeSpan.FromSeconds(6));
        }

        Assert.Equal(PlaybackPhase.Failed, _session.State.Phase);
        _session.Retry();
        Assert.Equal(PlaybackPhase.Tuning, _session.State.Phase);
        _engine.Raise(new EngineFailed("x"));
        Assert.Equal(1, _session.State.ReconnectAttempt);
    }

    [Fact]
    public void A_live_stream_ending_is_a_failure_that_reconnects_from_the_live_edge()
    {
        _session.Start(Channels());
        Begin();

        _engine.Raise(new EngineReachedEnd());
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2, _engine.Loads.Count);
        Assert.Null(_engine.Loads[1].Start);
    }

    [Fact]
    public void A_dropped_on_demand_connection_resumes_where_it_was()
    {
        _session.Start(new Lineup([Movie("m")], 0, "Movies"));
        Begin(duration: 3600, position: 600);

        _engine.Raise(new EngineReachedEnd());
        Assert.Equal(PlaybackPhase.Reconnecting, _session.State.Phase);
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(600, _engine.Loads[1].Start);
    }

    [Fact]
    public void A_truly_truncated_file_is_retried_once_then_accepted_as_ended()
    {
        _session.Start(new Lineup([Movie("m")], 0, "Movies"));
        Begin(duration: 3600, position: 600);
        _engine.Raise(new EngineReachedEnd());
        _time.Advance(TimeSpan.FromSeconds(1));

        Begin(duration: 3600, position: 603);
        _engine.Raise(new EngineReachedEnd());

        Assert.Equal(PlaybackPhase.Ended, _session.State.Phase);
    }

    [Fact]
    public void A_retry_before_the_first_frame_uses_the_original_resume_point()
    {
        _observer.Resume["m"] = 1200;
        _session.Start(new Lineup([Movie("m")], 0, "Movies"));
        Assert.Equal(1200, _engine.Loads[0].Start);

        _engine.Raise(new EngineFailed("timeout"));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1200, _engine.Loads[1].Start);
    }

    [Fact]
    public void Start_over_ignores_the_saved_resume_point()
    {
        _observer.Resume["m"] = 1200;
        _session.Start(new Lineup([Movie("m")], 0, "Movies"), resume: false);

        Assert.Null(_engine.Loads[0].Start);
    }

    [Fact]
    public void Progress_is_reported_every_5s_only_after_a_frame_and_final_on_switch()
    {
        _session.Start(new Lineup([Movie("a"), Movie("b")], 0, "Movies"));
        _engine.Raise(new EngineDuration(6000));
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(_observer.Reports);

        _engine.Raise(new EngineFirstFrame());
        _engine.Raise(new EnginePosition(100));
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal([("a", 100.0, false)], _observer.Reports.Select(r => (r.Item.Id, r.Position, r.Final)));

        _session.JumpTo(1);
        Assert.Equal(("a", 100.0, true), (_observer.Reports[^1].Item.Id, _observer.Reports[^1].Position, _observer.Reports[^1].Final));
    }

    [Fact]
    public void Live_progress_is_never_reported()
    {
        _session.Start(Channels());
        Begin(duration: 100, position: 50);
        _time.Advance(TimeSpan.FromSeconds(30));
        _session.Stop();

        Assert.Empty(_observer.Reports);
        Assert.Equal(PlaybackState.Idle, _session.State);
    }

    [Fact]
    public void Episodes_autoplay_after_an_8_second_countdown()
    {
        _session.Start(new Lineup([Episode(1), Episode(2)], 0, "Show"));
        Begin(duration: 2700, position: 2690);

        _engine.Raise(new EngineReachedEnd());
        Assert.Equal(PlaybackPhase.Ended, _session.State.Phase);
        Assert.Equal(8, _session.State.UpNextSecondsLeft);

        _time.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(1, _session.State.UpNextSecondsLeft);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(_session.State.UpNextSecondsLeft);
        Assert.Equal("e2", _session.State.Item?.Id);

        _time.Advance(PlaybackSession.ZapDebounce);
        Assert.Equal("http://h/series/e2.mp4", _engine.Loads[^1].Url);
    }

    [Fact]
    public void Seeking_back_from_the_end_cancels_the_countdown()
    {
        _session.Start(new Lineup([Episode(1), Episode(2)], 0, "Show"));
        Begin(duration: 2700, position: 2690);
        _engine.Raise(new EngineReachedEnd());

        _engine.Raise(new EngineLeftEnd());
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(_session.State.UpNextSecondsLeft);
        Assert.Equal("e1", _session.State.Item?.Id);
        Assert.Equal(PlaybackPhase.Playing, _session.State.Phase);
    }

    [Fact]
    public void Movies_and_other_shows_never_autoplay()
    {
        _session.Start(new Lineup([Episode(1, "a"), Episode(2, "b")], 0, "Continue Watching"));
        Begin(duration: 2700, position: 2690);
        _engine.Raise(new EngineReachedEnd());
        Assert.Null(_session.State.UpNextSecondsLeft);

        _session.Start(new Lineup([Movie("a"), Movie("b")], 0, "Movies"));
        Begin(duration: 2700, position: 2690);
        _engine.Raise(new EngineReachedEnd());
        Assert.Null(_session.State.UpNextSecondsLeft);

        _session.AutoplayEnabled = false;
        _session.Start(new Lineup([Episode(1), Episode(2)], 0, "Show"));
        Begin(duration: 2700, position: 2690);
        _engine.Raise(new EngineReachedEnd());
        Assert.Null(_session.State.UpNextSecondsLeft);
    }

    [Fact]
    public void Play_at_the_end_starts_again_from_zero()
    {
        _session.Start(new Lineup([Movie("m")], 0, "Movies"));
        Begin(duration: 100, position: 99);
        _engine.Raise(new EngineReachedEnd());

        _session.TogglePause();

        Assert.Equal(0, _engine.LastSeek);
        Assert.False(_engine.LastPaused);
    }

    [Fact]
    public void Disallowed_urls_fail_without_reaching_the_engine()
    {
        _session.Start(new Lineup([new PlaybackItem("x", ContentKind.Movie, "X", null, "file:///C:/secret.mkv")], 0, "T"));

        Assert.Empty(_engine.Loads);
        Assert.Equal(PlaybackPhase.Failed, _session.State.Phase);
        Assert.Equal(PlaybackSession.UnsupportedAddressMessage, _session.State.ErrorMessage);
    }

    [Fact]
    public void Jumping_to_a_disallowed_item_stops_what_was_playing()
    {
        _session.Start(new Lineup([Movie("ok"), new PlaybackItem("bad", ContentKind.Movie, "Bad", null, "edl://x.mkv")], 0, "T"));
        Begin(duration: 100, position: 10);

        _session.JumpTo(1);

        Assert.Equal(1, _engine.Stops);
        Assert.Equal(PlaybackPhase.Failed, _session.State.Phase);
    }

    [Fact]
    public void A_late_failure_from_the_previous_stream_does_not_touch_the_zapped_channel()
    {
        _session.Start(Channels());
        Begin();
        _session.Step(+1);

        _engine.Raise(new EngineFailed("old stream died"));
        _engine.Raise(new EngineReachedEnd());

        Assert.Equal(PlaybackPhase.Tuning, _session.State.Phase);
        Assert.Equal(0, _session.State.ReconnectAttempt);
        _time.Advance(PlaybackSession.ZapDebounce);
        Assert.Equal("http://h/live/1.ts", _engine.Loads[^1].Url);
    }

    private sealed class FakeEngine : IMediaEngine
    {
        public event EventHandler<EngineEvent>? EventRaised;

        public List<(string Url, bool Live, double? Start)> Loads { get; } = [];

        public int Stops { get; private set; }

        public double? LastSeek { get; private set; }

        public bool? LastPaused { get; private set; }

        public void Raise(EngineEvent e) => EventRaised?.Invoke(this, e);

        public void Load(string url, bool live, double? startSeconds) => Loads.Add((url, live, startSeconds));

        public void StopPlayback() => Stops++;

        public void SetPaused(bool paused) => LastPaused = paused;

        public void SeekAbsolute(double seconds) => LastSeek = seconds;
    }

    private sealed class FakeObserver : IPlaybackObserver
    {
        public List<(PlaybackItem Item, double Position, double Duration, bool Final)> Reports { get; } = [];

        public List<PlaybackItem> LiveStarts { get; } = [];

        public Dictionary<string, double> Resume { get; } = [];

        public void ReportProgress(PlaybackItem item, double position, double duration, bool isFinal) => Reports.Add((item, position, duration, isFinal));

        public void LiveChannelStarted(PlaybackItem item) => LiveStarts.Add(item);

        public double? ResumePositionFor(PlaybackItem item) => Resume.TryGetValue(item.Id, out var p) ? p : null;
    }
}
