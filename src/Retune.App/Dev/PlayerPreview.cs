using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Retune.App.Composition;
using Retune.App.Views;
using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Presentation.Player;

namespace Retune.App.Dev;

/// <summary>
/// Dev-only: renders the player chrome over a stand-in "video" picture in its main states, off-screen,
/// with a stub engine. Real video is verified separately (it lives in a native window WPF can't capture).
/// </summary>
internal static class PlayerPreview
{
    private static readonly Size Frame = new(1600, 900);

    public static async Task RenderAsync(AppContainer app, string folder)
    {
        var engine = new StubEngine();
        using var session = new PlaybackSession(engine, new LibraryPlaybackObserver(app.Library, app.Playlists), app.Time, a => Application.Current.Dispatcher.BeginInvoke(a));
        using var player = new PlayerViewModel(engine, session, app.Playlists, app.Settings, new StubAwake(), app.Time, a => Application.Current.Dispatcher.BeginInvoke(a), app.Log);
        var chrome = new PlayerView { DataContext = player };
        var root = new Grid { Background = FakeVideo() };
        root.Children.Add(chrome);

        // On-demand, playing, controls up.
        player.Play(new Lineup([new PlaybackItem("m1", ContentKind.Movie, "Northern Lantern (2008)", null, "http://127.0.0.1:9/movie/1.mp4")], 0, "Movies"));
        engine.Playing(duration: 6163, position: 1834, width: 1920, height: 1080);
        engine.SetTracks([new(1, TrackType.Audio, "eng", null, "aac", 6, true), new(2, TrackType.Audio, "deu", "Commentary", "aac", 2, false), new(3, TrackType.Subtitle, "eng", null, "srt", null, true)]);
        await Snap(root, Path.Combine(folder, "p1-movie.png")).ConfigureAwait(true);

        // Live with the channel list and stats.
        var channels = Enumerable.Range(1, 12).Select(n => new PlaybackItem($"c{n}", ContentKind.Live, $"{(n % 2 == 0 ? "Vertex" : "Nova")} News {n}", "News", $"http://127.0.0.1:9/live/{n}.ts", StreamId: $"c{n}", Number: n)).ToList();
        player.Play(new Lineup(channels, 3, "News"));
        engine.Playing(duration: 0, position: 0, width: 1280, height: 720);
        player.ToggleList();
        player.ToggleStats();
        await Snap(root, Path.Combine(folder, "p2-live-list-stats.png")).ConfigureAwait(true);
        player.Escape();
        player.Escape();

        // The shortcuts panel over the video.
        player.ToggleShortcuts();
        await Snap(root, Path.Combine(folder, "p5-shortcuts.png")).ConfigureAwait(true);
        player.Escape();

        // Zapping: the tuning card.
        player.Next();
        await Snap(root, Path.Combine(folder, "p3-tuning.png")).ConfigureAwait(true);

        // Failure after retries.
        for (var i = 0; i < 5; i++)
        {
            engine.Fail();
            app.Time.GetUtcNow();
        }

        await Snap(root, Path.Combine(folder, "p4-error.png"), settle: 300).ConfigureAwait(true);
        player.Close();
    }

    private static Task Snap(FrameworkElement element, string path, int settle = 600) => DevRenderer.Snap(element, path, Frame, settle);

    private static LinearGradientBrush FakeVideo()
    {
        var brush = new LinearGradientBrush(Color.FromRgb(40, 70, 110), Color.FromRgb(140, 60, 40), new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    private sealed class StubAwake : IKeepAwake
    {
        public void KeepAwake(bool awake)
        {
        }
    }

    private sealed class StubEngine : IPlayerEngine
    {
        public event EventHandler<EngineEvent>? EventRaised;

        public double Volume { get; private set; } = 80;

        public bool Muted => false;

        public double Speed { get; private set; } = 1;

        public IReadOnlyList<MediaTrack> Tracks { get; private set; } = [];

        public void Playing(double duration, double position, long width, long height)
        {
            Raise(new EngineFileLoaded());
            if (duration > 0)
            {
                Raise(new EngineDuration(duration));
            }

            Raise(new EngineFirstFrame());
            Raise(new EngineBuffering(false));
            Raise(new EnginePosition(position));
            Raise(new EngineVideoSize(width, height));
        }

        public void Fail() => Raise(new EngineFailed("connection refused"));

        public void SetTracks(IReadOnlyList<MediaTrack> tracks)
        {
            Tracks = tracks;
            Raise(new EngineTracksChanged());
        }

        public void Load(string url, bool live, double? startSeconds)
        {
        }

        public void StopPlayback()
        {
        }

        public void SetPaused(bool paused) => Raise(new EnginePaused(paused));

        public void SeekAbsolute(double seconds)
        {
        }

        public bool CanSeekAhead { get; set; } = true;

        public bool CanSeekTo(double seconds) => CanSeekAhead;

        public void SeekRelative(double seconds)
        {
        }

        public void SetVolume(double volume) => Volume = volume;

        public void SetMuted(bool muted)
        {
        }

        public void SetSpeed(double speed) => Speed = speed;

        public void SelectAudio(long id)
        {
        }

        public void SelectSubtitle(long? id)
        {
        }

        public void ApplySubtitleStyle(SubtitleStyle style)
        {
        }

        public void SetSubtitleDelay(double seconds)
        {
        }

        public PlaybackStats ReadStats() => new(1920, 1080, "h264", "yuv420p", false, "d3d11va", 25, 25, 0, 0, 4_200_000, "aac", "5.1", 48000, 192_000, 12.4, 1_250_000, 0.004);

        public Task<bool> ScreenshotAsync(string path) => Task.FromResult(false);

        private void Raise(EngineEvent e) => EventRaised?.Invoke(this, e);
    }
}
