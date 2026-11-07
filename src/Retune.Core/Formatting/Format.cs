using System.Globalization;

namespace Retune.Core.Formatting;

/// <summary>User-visible formatting shared by browse and player.</summary>
public static class Format
{
    private const double MaxClockSeconds = 7 * 24 * 3600;

    /// <summary><c>4:05</c> or <c>1:02:03</c>. Non-finite or negative input shows <c>0:00</c>.</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return "0:00";
        }

        var total = (long)Math.Floor(Math.Min(seconds, MaxClockSeconds));
        var (h, m, s) = (total / 3600, total % 3600 / 60, total % 60);
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }

    /// <summary><c>1h 42m</c> or <c>45m</c>; anything under a minute (including 0) shows <c>1m</c>.</summary>
    public static string Runtime(int seconds)
    {
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}h {m}m")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(m, 1)}m");
    }

    public static string Remaining(double seconds) =>
        $"{Runtime(double.IsFinite(seconds) && seconds > 0 ? (int)Math.Min(Math.Floor(seconds), MaxClockSeconds) : 0)} left";

    public static string Rating(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary><c>4.2 Mbps</c> at or above 1,000,000 bit/s, otherwise <c>640 kbps</c>.</summary>
    public static string Bitrate(double bitsPerSecond) =>
        bitsPerSecond >= 1_000_000
            ? string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1_000_000:0.0} Mbps")
            : string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond / 1000:0} kbps");

    /// <summary>The locale's short time, e.g. "21:30" or "9:30 PM".</summary>
    public static string Time(DateTimeOffset value, CultureInfo? culture = null) =>
        value.ToLocalTime().ToString("t", culture ?? CultureInfo.CurrentCulture);

    public static string TimeRange(DateTimeOffset start, DateTimeOffset end, CultureInfo? culture = null) =>
        $"{Time(start, culture)} – {Time(end, culture)}";
}

/// <summary>Screenshot file names come from the time only, never from (untrusted) titles.</summary>
public static class ScreenshotFile
{
    public static string Name(DateTime localTime) =>
        $"Retune {localTime.ToString("yyyy-MM-dd 'at' HH.mm.ss.fff", CultureInfo.InvariantCulture)}.png";

    /// <summary>Returns <paramref name="folder"/>/name, adding " (2)", " (3)"… so nothing is overwritten.</summary>
    public static string UniquePath(string folder, DateTime localTime, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var name = Name(localTime);
        var path = Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        for (var n = 2; exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}).png");
        }

        return path;
    }
}
