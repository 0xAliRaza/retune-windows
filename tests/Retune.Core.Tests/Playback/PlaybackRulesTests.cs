using Retune.Core.Formatting;
using Retune.Core.Models;
using Retune.Core.Playback;

namespace Retune.Core.Tests.Playback;

public sealed class PlaybackRulesTests
{
    [Theory]
    [InlineData(3840, 2160, "4K")]
    [InlineData(3840, 1600, "4K")]
    [InlineData(2560, 1440, "1440p")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1920, 800, "1080p")]
    [InlineData(1280, 720, "720p")]
    [InlineData(1280, 534, "720p")]
    [InlineData(720, 576, "SD")]
    [InlineData(0, 1080, null)]
    public void Quality_label_uses_width_and_height(long w, long h, string? expected)
    {
        Assert.Equal(expected, VideoQuality.Label(w, h));
    }

    [Theory]
    [InlineData(4_200_000, "4.2 Mbps")]
    [InlineData(1_000_000, "1.0 Mbps")]
    [InlineData(640_000, "640 kbps")]
    public void Bitrates_format_as_mbps_or_kbps(double bps, string expected)
    {
        Assert.Equal(expected, Format.Bitrate(bps));
    }

    [Theory]
    [InlineData(97, true, 100)]
    [InlineData(97, false, 95)]
    [InlineData(99.39, true, 100)]
    [InlineData(99.39, false, 95)]
    [InlineData(100, true, 100)]
    [InlineData(0, false, 0)]
    [InlineData(94, true, 95)]
    [InlineData(94, false, 90)]
    public void Volume_steps_snap_to_fives(double value, bool up, double expected)
    {
        Assert.Equal(expected, VolumeLevel.Stepped(value, up));
    }

    [Theory]
    [InlineData(99.39, "99%")]
    [InlineData(99.6, "100%")]
    public void Volume_label_rounds(double value, string expected)
    {
        Assert.Equal(expected, VolumeLevel.Label(value));
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-3, 0)]
    [InlineData(42.5, 43)]
    [InlineData(double.NaN, 100)]
    public void Stored_volume_is_clamped(double stored, double expected)
    {
        Assert.Equal(expected, VolumeLevel.Clamped(stored));
    }

    [Fact]
    public void Subtitle_style_clamps_and_maps_to_mpv()
    {
        var style = new SubtitleStyle(Scale: 9, Position: 10).Clamped();
        Assert.Equal(2.5, style.Scale);
        Assert.Equal(50, style.Position);

        var rounded = new SubtitleStyle(Scale: 1.24, Position: 87.6).Clamped();
        Assert.Equal(1.2, rounded.Scale);
        Assert.Equal(88, rounded.Position);

        var props = SubtitleStyle.Default.MpvProperties().ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("1.0", props["sub-scale"]);
        Assert.Equal("100.0", props["sub-pos"]);
        Assert.Equal("outline-and-shadow", props["sub-border-style"]);
        Assert.Equal("strip", props["sub-ass-override"]);

        var styled = new SubtitleStyle(Background: true, KeepsOriginalStyle: true).MpvProperties().ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal("background-box", styled["sub-border-style"]);
        Assert.Equal("scale", styled["sub-ass-override"]);
    }

    [Fact]
    public void Stream_profiles_set_the_same_properties_and_live_never_reconnects_inside_ffmpeg()
    {
        var live = StreamProfile.For(live: true).ToDictionary(p => p.Key, p => p.Value);
        var vod = StreamProfile.For(live: false).ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal(live.Keys.Order(), vod.Keys.Order());
        Assert.Equal(string.Empty, live["stream-lavf-o"]);
        Assert.DoesNotContain("reconnect_streamed", vod["stream-lavf-o"], StringComparison.Ordinal);
        Assert.Equal("128MiB", vod["demuxer-max-back-bytes"]);
        Assert.Equal(1.0, StreamProfile.ForcedSpeed(live: true));
        Assert.Null(StreamProfile.ForcedSpeed(live: false));
    }

    [Fact]
    public void Reconnect_backs_off_then_gives_up()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), ReconnectPolicy.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(6), ReconnectPolicy.DelayBefore(4));
        Assert.Null(ReconnectPolicy.DelayBefore(5));
        Assert.Null(ReconnectPolicy.DelayBefore(0));
        Assert.True(ReconnectPolicy.Delays.Aggregate(TimeSpan.Zero, (a, b) => a + b) >= TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(600, 3600, null, true)]
    [InlineData(3539, 3600, null, true)]
    [InlineData(3540, 3600, null, false)]
    [InlineData(600, 0, null, false)]
    [InlineData(600, 3600, 600.0, false)]
    [InlineData(605, 3600, 600.0, false)]
    [InlineData(606, 3600, 600.0, true)]
    public void Early_end_detects_dropped_connections(double position, double duration, double? previous, bool dropped)
    {
        Assert.Equal(dropped, EarlyEnd.IsDroppedConnection(position, duration, previous));
    }

    [Theory]
    [InlineData(1.0, true, 1.25)]
    [InlineData(1.0, false, 0.75)]
    [InlineData(3.0, true, 3.0)]
    [InlineData(0.5, false, 0.5)]
    [InlineData(1.1, true, 1.5)]
    public void Speed_steps_through_presets(double current, bool faster, double expected)
    {
        Assert.Equal(expected, SpeedPresets.Step(current, faster));
    }

    [Theory]
    [InlineData(10, 6000, false, false)]
    [InlineData(600, 6000, true, false)]
    [InlineData(5800, 6000, false, true)]
    [InlineData(29.9, 6000, false, false)]
    [InlineData(30, 60, false, true)]
    public void Progress_thresholds(double position, double duration, bool resumable, bool finished)
    {
        var p = new WatchProgress(new PlaybackItem("1", ContentKind.Movie, "M", null, "retune://movie/1.mp4"), position, duration, DateTimeOffset.UnixEpoch);

        Assert.Equal(resumable, p.IsResumable);
        Assert.Equal(finished, p.IsFinished);
    }

    [Fact]
    public void Progress_fraction_and_resume_point()
    {
        var p = new WatchProgress(new PlaybackItem("1", ContentKind.Movie, "M", null, "u"), 3000, 6000, DateTimeOffset.UnixEpoch);

        Assert.Equal(0.5, p.Fraction);
        Assert.Equal(2995, p.ResumePosition);
        Assert.Equal(3000, p.Remaining);
        Assert.Equal(0, (p with { Duration = 0 }).Fraction);
    }
}

public sealed class FormatTests
{
    [Theory]
    [InlineData(245, "4:05")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-1, "0:00")]
    [InlineData(double.NaN, "0:00")]
    [InlineData(double.PositiveInfinity, "0:00")]
    [InlineData(59.9, "0:59")]
    [InlineData(1e12, "168:00:00")]
    public void Clock(double seconds, string expected)
    {
        Assert.Equal(expected, Format.Clock(seconds));
    }

    [Theory]
    [InlineData(6120, "1h 42m")]
    [InlineData(2700, "45m")]
    [InlineData(0, "1m")]
    [InlineData(59, "1m")]
    public void Runtime(int seconds, string expected)
    {
        Assert.Equal(expected, Format.Runtime(seconds));
    }

    [Fact]
    public void Remaining_and_rating()
    {
        Assert.Equal("1h 12m left", Format.Remaining(4320.7));
        Assert.Equal("1m left", Format.Remaining(double.NaN));
        Assert.Equal("1m left", Format.Remaining(-30));
        Assert.Equal("8.4", Format.Rating(8.4));
    }

    [Fact]
    public void Screenshot_names_come_from_the_time_only_and_never_overwrite()
    {
        var at = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);
        Assert.Equal("Retune 1970-01-01 at 00.00.00.000.png", ScreenshotFile.Name(at));

        var taken = new HashSet<string> { Path.Combine("C:\\shots", "Retune 1970-01-01 at 00.00.00.000.png") };
        Assert.Equal(Path.Combine("C:\\shots", "Retune 1970-01-01 at 00.00.00.000 (2).png"), ScreenshotFile.UniquePath("C:\\shots", at, taken.Contains));
    }

    [Theory]
    [InlineData("The Dark Knight", "TD")]
    [InlineData("  amélie ", "A")]
    [InlineData("24: Live Another Day", "2L")]
    [InlineData("東京 物語", "東物")]
    [InlineData("", "")]
    public void Placeholder_initials(string title, string expected)
    {
        Assert.Equal(expected, ArtworkPlaceholder.Initials(title));
    }

    [Fact]
    public void Placeholder_colour_is_stable_and_in_range()
    {
        Assert.Equal(ArtworkPlaceholder.Seed("Batman"), ArtworkPlaceholder.Seed("Batman"));
        Assert.NotEqual(ArtworkPlaceholder.Seed("Batman"), ArtworkPlaceholder.Seed("Robin"));
        Assert.InRange(ArtworkPlaceholder.Seed(new string('z', 10_000)), 0, 0xFFFF);
        var (from, to) = ArtworkPlaceholder.Gradient("Batman");
        Assert.InRange(from.H, 0, 1);
        Assert.InRange(to.H, 0, 1);
    }
}
