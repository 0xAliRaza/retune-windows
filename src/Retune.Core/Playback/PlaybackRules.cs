using System.Globalization;

namespace Retune.Core.Playback;

/// <summary>Quality badge from the decoded frame size. Width matters as much as height (scope films are short).</summary>
public static class VideoQuality
{
    public static string? Label(long width, long height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return (width, height) switch
        {
            _ when width >= 3200 || height >= 2000 => "4K",
            _ when width >= 2200 || height >= 1400 => "1440p",
            _ when width >= 1700 || height >= 1000 => "1080p",
            _ when width >= 1200 || height >= 700 => "720p",
            _ => "SD",
        };
    }
}

/// <summary>Volume is 0–100 with no amplification, stepped in fives by the keyboard.</summary>
public static class VolumeLevel
{
    public const double Max = 100;
    public const double Step = 5;
    public const double Default = 100;

    public static double Clamped(double value) =>
        double.IsFinite(value) ? Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, Max) : Default;

    /// <summary>Snaps to the next multiple of five in the given direction: 97 → 100 / 95.</summary>
    public static double Stepped(double value, bool up)
    {
        var next = up
            ? (Math.Floor(value / Step) + 1) * Step
            : (Math.Ceiling(value / Step) - 1) * Step;
        return Math.Clamp(next, 0, Max);
    }

    public static string Label(double value) => $"{Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}%";
}

/// <summary>Playback speed presets (on-demand only; live always plays at 1×).</summary>
public static class SpeedPresets
{
    public static IReadOnlyList<double> All { get; } = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3];

    /// <summary>Steps from the first preset at or above the current speed (with a small tolerance).</summary>
    public static double Step(double current, bool faster)
    {
        var index = -1;
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i] >= current - 0.01)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = 2; // 1×
        }

        return All[Math.Clamp(index + (faster ? 1 : -1), 0, All.Count - 1)];
    }

    public static string Label(double speed) =>
        Math.Abs(speed - 1) < 0.001 ? "Normal" : $"{speed.ToString("0.##", CultureInfo.InvariantCulture)}×";
}

/// <summary>Subtitle look, persisted globally and applied live.</summary>
public sealed record SubtitleStyle(double Scale = 1.0, double Position = 100, bool Background = false, bool KeepsOriginalStyle = false)
{
    public const double MinScale = 0.5;
    public const double MaxScale = 2.5;
    public const double MinPosition = 50;
    public const double MaxPosition = 100;

    public static SubtitleStyle Default { get; } = new();

    public SubtitleStyle Clamped() => this with
    {
        Scale = Math.Clamp(Math.Round(Scale, 1, MidpointRounding.AwayFromZero), MinScale, MaxScale),
        Position = Math.Clamp(Math.Round(Position, MidpointRounding.AwayFromZero), MinPosition, MaxPosition),
    };

    /// <summary>The mpv properties this style sets.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> MpvProperties()
    {
        var s = Clamped();
        return
        [
            new("sub-scale", s.Scale.ToString("0.0", CultureInfo.InvariantCulture)),
            new("sub-pos", s.Position.ToString("0.0", CultureInfo.InvariantCulture)),
            new("sub-border-style", s.Background ? "background-box" : "outline-and-shadow"),
            new("sub-ass-override", s.KeepsOriginalStyle ? "scale" : "strip"),
        ];
    }
}

/// <summary>
/// Per-load mpv properties. Both profiles set the same names, because mpv keeps properties across files.
/// Live never uses FFmpeg reconnect: it splices the new connection into the demuxer and replays old video.
/// </summary>
public static class StreamProfile
{
    public static IReadOnlyList<KeyValuePair<string, string>> For(bool live) => live
        ? [new("stream-lavf-o", string.Empty), new("demuxer-max-back-bytes", "0"), new("demuxer-seekable-cache", "no")]
        : [new("stream-lavf-o", "reconnect=1,reconnect_delay_max=5"), new("demuxer-max-back-bytes", "128MiB"), new("demuxer-seekable-cache", "auto")];

    /// <summary>Live always loads at 1×; on-demand keeps the viewer's speed (null = leave as is).</summary>
    public static double? ForcedSpeed(bool live) => live ? 1.0 : null;
}

/// <summary>Automatic retry schedule for failed or dropped streams.</summary>
public static class ReconnectPolicy
{
    public static IReadOnlyList<TimeSpan> Delays { get; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)];

    public static int MaxAttempts => Delays.Count;

    /// <summary>The wait before retry number <paramref name="attempt"/> (1-based), or null when exhausted.</summary>
    public static TimeSpan? DelayBefore(int attempt) => attempt >= 1 && attempt <= Delays.Count ? Delays[attempt - 1] : null;
}

/// <summary>
/// FFmpeg reports a dropped on-demand connection as a normal end of file. An end with more than a minute
/// left is treated as a drop, unless the previous early end didn't get at least 5 s further (a truly
/// truncated file is retried once, then accepted).
/// </summary>
public static class EarlyEnd
{
    public const double MinRemainingSeconds = 60;
    public const double MinProgressSeconds = 5;

    public static bool IsDroppedConnection(double position, double duration, double? previousEarlyEnd) =>
        duration > 0
        && duration - position > MinRemainingSeconds
        && (previousEarlyEnd is not { } previous || position - previous > MinProgressSeconds);
}
