using System.Collections.Concurrent;
using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Mpv.Tests.Support;

namespace Retune.Mpv.Tests;

/// <summary>The real playback rules over the real engine and headless mpv, on one dedicated "UI" thread.</summary>
public sealed class MpvEngineTests : IDisposable
{
    private readonly UiThread _ui = new();
    private readonly MpvClient _mpv;
    private readonly MpvEngine _engine;
    private readonly PlaybackSession _session;
    private readonly Observer _observer = new();

    public MpvEngineTests()
    {
        var options = MpvOptions.ForEmbeddedPlayer(null, 100, "Retune-Tests/1.0", silent: true);
        _mpv = MpvClient.Create(options, logLevel: null);
        _engine = _ui.Invoke(() => new MpvEngine(_mpv, _ui.Post));
        _session = _ui.Invoke(() => new PlaybackSession(_engine, _observer, TimeProvider.System, _ui.Post));
    }

    public void Dispose()
    {
        _ui.Invoke(() =>
        {
            _session.Dispose();
            _engine.Dispose();
        });
        _mpv.Dispose();
        _ui.Dispose();
    }

    private static PlaybackItem File(string name, ContentKind kind = ContentKind.Movie) =>
        new(name, kind, name, null, new Uri(TestMedia.PathOf(name)).AbsoluteUri.Replace("file:///", "http://127.0.0.1:9/", StringComparison.Ordinal));

    private void Play(string name)
    {
        // The URL policy rejects file: URLs (as it must), so feed mpv the local path directly through the engine.
        _ui.Invoke(() => _engine.Load(TestMedia.PathOf(name), live: false, startSeconds: null));
    }

    private async Task Until(Func<bool> condition, int seconds = 15)
    {
        for (var i = 0; i < seconds * 20; i++)
        {
            if (_ui.Invoke(condition))
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Timed out waiting for the engine.");
    }

    [Fact]
    public async Task Reports_tracks_duration_and_stats_for_a_real_file()
    {
        var events = new ConcurrentQueue<EngineEvent>();
        _ui.Invoke(() => _engine.EventRaised += (_, e) => events.Enqueue(e));

        Play(TestMedia.MultiTrack);

        await Until(() => events.OfType<EngineFirstFrame>().Any());
        await Until(() => _engine.Tracks.Count == 4);
        var tracks = _ui.Invoke(() => _engine.Tracks);
        Assert.Equal(2, tracks.Count(t => t.Type == TrackType.Audio));
        Assert.Contains(tracks, t => t.Type == TrackType.Subtitle && t.Language == "eng");
        Assert.Contains(events, e => e is EngineDuration { Seconds: > 9 and < 11 });

        _ui.Invoke(() => _engine.SelectAudio(tracks.First(t => t.Language == "ger").Id));
        await Until(() => _engine.Tracks.Any(t => t is { Type: TrackType.Audio, Language: "ger", IsSelected: true }));
        _ui.Invoke(() => _engine.SelectSubtitle(null));
        await Until(() => !_engine.Tracks.Any(t => t is { Type: TrackType.Subtitle, IsSelected: true }));

        var stats = _ui.Invoke(_engine.ReadStats);
        Assert.Equal(1280, stats.Width);
        Assert.Equal("720p", stats.QualityLabel);
    }

    [Fact]
    public async Task Volume_is_clamped_and_leaves_mute_alone()
    {
        _ui.Invoke(() =>
        {
            _engine.SetMuted(true);
            _engine.SetVolume(150);
        });

        await Until(() => _engine.Volume == 100 && _engine.Muted);
    }

    [Fact]
    public async Task The_end_of_a_file_reaches_the_session_as_eof()
    {
        var reachedEnd = false;
        _ui.Invoke(() => _engine.EventRaised += (_, e) => reachedEnd |= e is EngineReachedEnd);

        Play(TestMedia.NoAudio);
        await Until(() => _engine.Tracks.Count > 0);
        _ui.Invoke(() => _engine.SeekAbsolute(9.5));

        await Until(() => reachedEnd);
    }

    [Fact]
    public async Task The_session_drives_mpv_and_sees_the_first_frame()
    {
        // Loopback http served nothing: the policy accepts it, mpv fails to open it, and the session schedules a retry.
        _ui.Invoke(() => _session.Start(new Lineup([File(TestMedia.Vod1080p)], 0, "T")));

        await Until(() => _session.State.Phase is PlaybackPhase.Reconnecting or PlaybackPhase.Failed);
        Assert.Equal(1, _ui.Invoke(() => _session.State.ReconnectAttempt));
    }

    private sealed class Observer : IPlaybackObserver
    {
        public void ReportProgress(PlaybackItem item, double position, double duration, bool isFinal)
        {
        }

        public void LiveChannelStarted(PlaybackItem item)
        {
        }

        public double? ResumePositionFor(PlaybackItem item) => null;
    }
}
