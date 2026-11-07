using System.Text;

namespace Retune.Core.Formatting;

/// <summary>
/// Stable initials and colours for items without artwork. The seed is a fixed fold over the title's
/// Unicode scalars, so a title keeps the same colour across launches (string.GetHashCode is randomised).
/// </summary>
public static class ArtworkPlaceholder
{
    /// <summary>First letter of each of the first two words (split on non letters/digits), uppercased.</summary>
    public static string Initials(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var initials = new List<Rune>(2);
        var inWord = false;
        foreach (var rune in title.EnumerateRunes())
        {
            var isWordChar = Rune.IsLetterOrDigit(rune);
            if (isWordChar && !inWord)
            {
                initials.Add(Rune.ToUpperInvariant(rune));
                if (initials.Count == 2)
                {
                    break;
                }
            }

            inWord = isWordChar;
        }

        return string.Concat(initials.Select(r => r.ToString()));
    }

    public static int Seed(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var seed = 0;
        foreach (var rune in title.EnumerateRunes())
        {
            seed = unchecked((seed * 31) + rune.Value) & 0xFFFF;
        }

        return seed;
    }

    /// <summary>Hue in 0–1.</summary>
    public static double Hue(string title) => Seed(title) % 360 / 360.0;

    /// <summary>Gradient end points as HSB triples (hue, saturation, brightness), all 0–1.</summary>
    public static ((double H, double S, double B) From, (double H, double S, double B) To) Gradient(string title)
    {
        var hue = Hue(title);
        return ((hue, 0.45, 0.45), ((hue + 0.08) % 1.0, 0.55, 0.28));
    }
}
