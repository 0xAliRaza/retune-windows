using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using Retune.App.Interop;

namespace Retune.App.Dev;

/// <summary>
/// Developer command-line options for unattended runs that never disturb the user:
/// <c>--no-activate</c>, <c>--window x,y,w,h</c> (physical pixels), and
/// <c>--render &lt;folder&gt; --demo &lt;provider url&gt;</c> to save screenshots off-screen without showing any window.
/// </summary>
/// <param name="Mute">No audio device at all (unattended runs must never make sound).</param>
/// <param name="AutoPlay">Play the first visible item once the demo playlist has loaded.</param>
/// <param name="TourFolder">With <c>--demo</c>: run <see cref="ScreenshotTour"/> and save the screenshots here.</param>
/// <param name="PerfReport">With <c>--demo</c>: run <see cref="PerfProbe"/> and write its report here.</param>
public sealed record DevOptions(bool NoActivate, (int X, int Y, int Width, int Height)? WindowBounds, string? RenderFolder, string? DemoProvider, bool Mute = false, bool AutoPlay = false, string? PerfReport = null, string? TourFolder = null)
{
    /// <summary>Dev flags only count in isolated runs (<c>RETUNE_DATA_DIR</c>), so they can't touch a user's real data.</summary>
    public static DevOptions Parse(IReadOnlyList<string> args, bool isolated)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!isolated)
        {
            return new DevOptions(false, null, null, null);
        }

        string? ValueAfter(string flag)
        {
            for (var i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        static bool Int(string s, out int value) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        var bounds = ValueAfter("--window")?.Split(',') is { Length: 4 } p && Int(p[0], out var x) && Int(p[1], out var y) && Int(p[2], out var w) && Int(p[3], out var h)
            ? (x, y, w, h)
            : ((int, int, int, int)?)null;
        return new DevOptions(args.Contains("--no-activate"), bounds, ValueAfter("--render"), ValueAfter("--demo"), args.Contains("--mute"), args.Contains("--autoplay"), ValueAfter("--perf"), ValueAfter("--tour"));
    }

    public void ApplyBounds(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (WindowBounds is not { } b)
        {
            return;
        }

        window.SourceInitialized += (_, _) =>
            NativeMethods.SetWindowPos(new WindowInteropHelper(window).Handle, 0, b.X, b.Y, b.Width, b.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
    }
}
