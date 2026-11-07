using System.Globalization;

namespace Retune.App;

/// <summary>A scripted action for unattended dev runs, executed <see cref="At"/> after startup.</summary>
public sealed record DevStep(TimeSpan At, string Action);

/// <summary>
/// Developer command-line options, so checks can run unattended without stealing the user's
/// keyboard focus, sound or main screen:
/// <c>--play &lt;path&gt;</c>, <c>--silent</c>, <c>--no-activate</c>,
/// <c>--window x,y,w,h</c> (physical pixels) and <c>--spike-steps block@5,fullscreen@9,quit@17</c>.
/// </summary>
public sealed record DevOptions(
    string? MediaPath,
    bool Silent,
    bool NoActivate,
    (int X, int Y, int Width, int Height)? WindowBounds,
    IReadOnlyList<DevStep> Steps)
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

        return new DevOptions(
            ValueAfter("--play"),
            args.Contains("--silent"),
            args.Contains("--no-activate"),
            ParseBounds(ValueAfter("--window")),
            ParseSteps(ValueAfter("--spike-steps")));
    }

    private static (int, int, int, int)? ParseBounds(string? value)
    {
        var parts = value?.Split(',');
        if (parts is not { Length: 4 })
        {
            return null;
        }

        var n = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return (n[0], n[1], n[2], n[3]);
    }

    private static List<DevStep> ParseSteps(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(step => step.Split('@'))
                .Select(p => new DevStep(TimeSpan.FromSeconds(double.Parse(p[1], CultureInfo.InvariantCulture)), p[0]))
                .ToList();
}
