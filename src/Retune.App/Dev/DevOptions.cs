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
/// <param name="Mute">Start the player muted (unattended runs must never make sound).</param>
/// <param name="AutoPlay">Play the first visible item once the demo playlist has loaded.</param>
public sealed record DevOptions(bool NoActivate, (int X, int Y, int Width, int Height)? WindowBounds, string? RenderFolder, string? DemoProvider, bool Mute = false, bool AutoPlay = false)
{
    public static DevOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
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

        var bounds = ValueAfter("--window")?.Split(',') is { Length: 4 } p
            ? (int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3], CultureInfo.InvariantCulture))
            : ((int, int, int, int)?)null;
        return new DevOptions(args.Contains("--no-activate"), bounds, ValueAfter("--render"), ValueAfter("--demo"), args.Contains("--mute"), args.Contains("--autoplay"));
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
