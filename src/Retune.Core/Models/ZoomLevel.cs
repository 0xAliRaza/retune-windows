using System.Globalization;

namespace Retune.Core.Models;

/// <summary>
/// App-wide zoom (Ctrl+Plus / Ctrl+Minus / Ctrl+0, Ctrl+mouse wheel, Settings): fixed steps like a browser, so
/// repeated presses always land on the same, tidy sizes.
/// </summary>
public static class ZoomLevel
{
    public const double Default = 1;

    public static IReadOnlyList<double> Steps { get; } = [0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2];

    public static double Min => Steps[0];

    public static double Max => Steps[^1];

    /// <summary>Unknown or broken values (a hand-edited settings file) become 100%; others are kept within range.</summary>
    public static double Clamped(double value) => double.IsFinite(value) ? Math.Clamp(value, Min, Max) : Default;

    /// <summary>The next step above the current zoom (or the largest).</summary>
    public static double In(double current)
    {
        var value = Clamped(current);
        return Steps.FirstOrDefault(s => s > value + 0.001, Max);
    }

    /// <summary>The next step below the current zoom (or the smallest).</summary>
    public static double Out(double current)
    {
        var value = Clamped(current);
        return Steps.LastOrDefault(s => s < value - 0.001, Min);
    }

    public static string Label(double value) =>
        $"{Math.Round(Clamped(value) * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}%";
}
