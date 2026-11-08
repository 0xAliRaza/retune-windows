using System.Globalization;
using System.Text;

namespace Retune.Core.Search;

/// <summary>
/// Case-, accent- and width-insensitive matching without locale rules: "amelie" finds "Amélie",
/// "ｂａｔｍａｎ" finds "Batman", "strasse" finds "Straße". Names are normalised once per item; a query
/// matches when every whitespace-separated term is a substring of the normalised name.
/// </summary>
public static class SearchText
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsSimpleAscii(text))
        {
            return text.ToLowerInvariant();
        }

        // NFKC folds width and compatibility forms (ﬁ → fi, ｂ → b); NFD then splits accents off their letters.
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            // Invalid UTF-16 (a lone surrogate pasted into the search box): fold case only.
            return text.ToLowerInvariant();
        }
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            switch (lower)
            {
                // Letters with no decomposition that users type without the mark.
                case 'ß': builder.Append("ss"); break;
                case 'æ': builder.Append("ae"); break;
                case 'œ': builder.Append("oe"); break;
                case 'ø': builder.Append('o'); break;
                case 'ł': builder.Append('l'); break;
                case 'đ': builder.Append('d'); break;
                case 'ı': builder.Append('i'); break;
                default: builder.Append(lower); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>The normalised, whitespace-separated terms of a query (empty for a blank query).</summary>
    public static string[] Terms(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when every term occurs in <paramref name="normalizedName"/> (AND, any order).</summary>
    public static bool Matches(string normalizedName, ReadOnlySpan<string> terms)
    {
        ArgumentNullException.ThrowIfNull(normalizedName);
        foreach (var term in terms)
        {
            if (!normalizedName.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static bool Matches(string name, string? query) => Matches(Normalize(name), Terms(query));

    /// <summary>Most catalog names are plain ASCII: skip Unicode normalisation for them.</summary>
    private static bool IsSimpleAscii(string text)
    {
        foreach (var c in text)
        {
            if (c > 0x7E)
            {
                return false;
            }
        }

        return true;
    }
}
