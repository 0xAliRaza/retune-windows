using System.Text;

namespace Retune.Core.Policies;

/// <summary>Strict UTF-8 percent-encoding for the two places Retune builds URLs from user data.</summary>
public static class PercentEncoding
{
    private const string Unreserved = "-._~";

    /// <summary>RFC 3986 sub-delims plus ':' and '@': allowed in a path segment. '/' is not.</summary>
    private const string PathSegmentExtras = "!$&'()*+,;=:@";

    /// <summary>
    /// Encodes a query value so that only alphanumerics and <c>-._~</c> stay literal.
    /// PHP reads a raw '+' as a space, so it must become %2B.
    /// </summary>
    public static string QueryValue(string value) => Encode(value, static c => IsAsciiAlphanumeric(c) || Unreserved.Contains(c, StringComparison.Ordinal));

    /// <summary>Encodes one path segment, including '/', so credentials can't add path segments.</summary>
    public static string PathSegment(string value) =>
        Encode(value, static c => IsAsciiAlphanumeric(c) || Unreserved.Contains(c, StringComparison.Ordinal) || PathSegmentExtras.Contains(c, StringComparison.Ordinal));

    private static bool IsAsciiAlphanumeric(char c) => char.IsAsciiLetterOrDigit(c);

    /// <summary>Percent-encodes the UTF-8 bytes of every character that isn't an ASCII literal per <paramref name="isLiteral"/>.</summary>
    internal static string Encode(string value, Func<char, bool> isLiteral)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length + 8);
        Span<byte> utf8 = stackalloc byte[4];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c < 0x80 && isLiteral(c))
            {
                builder.Append(c);
                continue;
            }

            var length = char.IsSurrogatePair(value, i)
                ? Encoding.UTF8.GetBytes(value.AsSpan(i++, 2), utf8)
                : Encoding.UTF8.GetBytes(value.AsSpan(i, 1), utf8);
            foreach (var b in utf8[..length])
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
