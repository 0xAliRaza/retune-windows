using System.Diagnostics;
using Retune.Mpv.Tests.Support;

namespace Retune.Mpv.Tests;

public sealed class MpvClientTests
{
    [Fact]
    public void Loaded_libmpv_speaks_client_api_2()
    {
        Assert.Equal(2, MpvClient.ApiVersion.Major);
    }

    [Fact]
    public async Task Plays_a_local_file_and_reports_its_duration()
    {
        using var player = new HeadlessPlayer();
        player.Client.Observe("duration", MpvPropertyFormat.Number);

        player.Load(TestMedia.PathOf(TestMedia.Vod1080p));

        await player.WaitFor<MpvFileStarted>();
        await player.WaitFor<MpvFileLoaded>();
        await player.WaitFor<MpvPlaybackRestarted>();
        var duration = await player.WaitForProperty("duration", v => v is double);
        Assert.InRange((double)duration.Value!, 59.5, 60.5);
    }

    [Fact]
    public async Task Reports_a_missing_file_as_an_error_end()
    {
        using var player = new HeadlessPlayer();

        player.Load(Path.Combine(Path.GetTempPath(), $"retune-missing-{Guid.NewGuid():N}.mp4"));

        var end = await player.WaitFor<MpvEndFile>();
        Assert.Equal(MpvEndFileReason.Error, end.Reason);
        Assert.True(end.ErrorCode < 0);
        Assert.False(string.IsNullOrWhiteSpace(end.ErrorMessage));
    }

    [Fact]
    public async Task Plays_paths_with_spaces_and_non_ascii_characters()
    {
        var folder = Directory.CreateTempSubdirectory("retune test Ünïcødé ");
        try
        {
            var copy = Path.Combine(folder.FullName, "chaîne été 東京.ts");
            File.Copy(TestMedia.PathOf(TestMedia.Live720p), copy);
            using var player = new HeadlessPlayer();

            player.Load(copy);

            await player.WaitFor<MpvPlaybackRestarted>();
            Assert.DoesNotContain(player.History, e => e is MpvEndFile { Reason: MpvEndFileReason.Error });
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Exposes_every_track_of_a_multi_track_file()
    {
        using var player = new HeadlessPlayer();

        player.Load(TestMedia.PathOf(TestMedia.MultiTrack));
        await player.WaitFor<MpvFileLoaded>();

        Assert.Equal(4, player.Client.GetInt64("track-list/count"));
        Assert.Equal("ger", player.Client.GetString("track-list/2/lang"));
        Assert.Equal("sub", player.Client.GetString("track-list/3/type"));
    }

    [Fact]
    public async Task A_file_without_audio_has_only_a_video_track()
    {
        using var player = new HeadlessPlayer();

        player.Load(TestMedia.PathOf(TestMedia.NoAudio));
        await player.WaitFor<MpvFileLoaded>();

        Assert.Equal(1, player.Client.GetInt64("track-list/count"));
        Assert.Equal("video", player.Client.GetString("track-list/0/type"));
    }

    [Fact]
    public async Task Typed_properties_round_trip()
    {
        using var player = new HeadlessPlayer();
        player.Load(TestMedia.PathOf(TestMedia.Vod1080p));
        await player.WaitFor<MpvFileLoaded>();

        player.Client.SetProperty("pause", true);
        player.Client.SetProperty("volume", 42.0);
        player.Client.SetProperty("speed", "1.5");

        Assert.True(player.Client.GetFlag("pause"));
        Assert.Equal(42.0, player.Client.GetDouble("volume"));
        Assert.Equal(1.5, player.Client.GetDouble("speed"));
    }

    [Fact]
    public void Volume_can_never_be_amplified_past_100()
    {
        using var player = new HeadlessPlayer();

        Assert.Throws<MpvException>(() => player.Client.SetProperty("volume", 150.0));
        Assert.Equal(100, player.Client.GetDouble("volume-max"));
    }

    [Fact]
    public void Security_options_are_in_force()
    {
        using var player = new HeadlessPlayer();

        Assert.Equal("no", player.Client.GetString("load-scripts"));
        Assert.Equal("no", player.Client.GetString("ytdl"));
        Assert.Equal("no", player.Client.GetString("load-unsafe-playlists"));
    }

    [Fact]
    public void An_unknown_command_throws_with_the_mpv_error()
    {
        using var player = new HeadlessPlayer();

        var ex = Assert.Throws<MpvException>(() => player.Client.Command("no-such-command"));
        Assert.True(ex.ErrorCode < 0);
    }

    [Fact]
    public void An_invalid_startup_option_fails_creation()
    {
        var ex = Assert.Throws<MpvException>(() =>
            MpvClient.Create([new("no-such-option", "1")], logLevel: null));
        Assert.Contains("no-such-option", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_stop_the_event_loop()
    {
        using var player = new HeadlessPlayer();
        player.Client.EventReceived += (_, _) => throw new InvalidOperationException("faulty subscriber");

        player.Load(TestMedia.PathOf(TestMedia.Vod1080p));

        await player.WaitFor<MpvPlaybackRestarted>();
    }

    [Fact]
    public async Task Disposing_from_an_event_handler_is_rejected_instead_of_corrupting_the_loop()
    {
        using var player = new HeadlessPlayer();
        var rejected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Client.EventReceived += (_, e) =>
        {
            if (e is MpvFileStarted)
            {
                try
                {
                    player.Client.Dispose();
                    rejected.TrySetResult(null);
                }
                catch (InvalidOperationException ex)
                {
                    rejected.TrySetResult(ex);
                }
            }
        };

        player.Load(TestMedia.PathOf(TestMedia.Vod1080p));

        Assert.IsType<InvalidOperationException>(await rejected.Task.WaitAsync(HeadlessPlayer.DefaultTimeout, TestContext.Current.CancellationToken));
        await player.WaitFor<MpvPlaybackRestarted>();
    }

    [Fact]
    public async Task Dispose_during_playback_is_prompt_and_idempotent()
    {
        var player = new HeadlessPlayer();
        player.Load(TestMedia.PathOf(TestMedia.Vod1080p));
        await player.WaitFor<MpvPlaybackRestarted>();

        var watch = Stopwatch.StartNew();
        player.Dispose();
        player.Dispose();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Dispose took {watch.Elapsed}.");
        Assert.Throws<ObjectDisposedException>(() => player.Client.GetFlag("pause"));
    }
}
