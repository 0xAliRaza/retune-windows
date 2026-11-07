using System.Globalization;
using System.Text.Json;

namespace Retune.Core.Xtream;

/// <summary>
/// Never-throwing readers for provider JSON, where any field may be missing, null, a string, a number
/// or a bool. A wrong type simply gives null. Spec: docs/spec/core.md §2.6.
/// </summary>
internal static class JsonLenient
{
    /// <summary>Dates must be after 1970 and before year 10000.</summary>
    private const double MaxUnixSeconds = 253_402_300_800;

    public static JsonElement? Field(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    /// <summary>A string, or a number/bool rendered as one (ints in decimal, bools as 1/0).</summary>
    public static string? LenientString(this JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.String } e => e.GetString(),
        { ValueKind: JsonValueKind.Number } e => e.TryGetInt64(out var i)
            ? i.ToString(CultureInfo.InvariantCulture)
            : e.GetRawText(),
        { ValueKind: JsonValueKind.True } => "1",
        { ValueKind: JsonValueKind.False } => "0",
        _ => null,
    };

    /// <summary><see cref="LenientString"/>, trimmed; blank becomes null. Used for almost every text field.</summary>
    public static string? LenientText(this JsonElement? element) =>
        element.LenientString()?.Trim() is { Length: > 0 } text ? text : null;

    /// <summary>Whole numbers from ints, finite doubles (truncated) or numeric strings. Bools are not numbers.</summary>
    public static long? LenientInt64(this JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Number } e => e.TryGetInt64(out var i) ? i : e.TryGetDouble(out var d) ? Truncate(d) : null,
        { ValueKind: JsonValueKind.String } e => ParseWhole(e.GetString()),
        _ => null,
    };

    public static int? LenientInt(this JsonElement? element) =>
        element.LenientInt64() is { } v && v is >= int.MinValue and <= int.MaxValue ? (int)v : null;

    public static double? LenientDouble(this JsonElement? element)
    {
        var value = element switch
        {
            { ValueKind: JsonValueKind.Number } e => e.TryGetDouble(out var d) ? d : null,
            { ValueKind: JsonValueKind.String } e => ParseDouble(e.GetString()),
            _ => null,
        };
        return value is { } v && double.IsFinite(v) ? v : null;
    }

    /// <summary>JSON bools as-is; otherwise any non-zero number. "yes", null and missing are false.</summary>
    public static bool LenientBool(this JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => element.LenientInt64() is { } v && v != 0,
    };

    /// <summary>Unix seconds (number or string) within 1970–9999.</summary>
    public static DateTimeOffset? LenientDate(this JsonElement? element) =>
        element.LenientDouble() is { } seconds && seconds > 0 && seconds < MaxUnixSeconds
            ? DateTimeOffset.UnixEpoch.AddSeconds(seconds)
            : null;

    public static Uri? LenientUrl(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } e ? Policies.LenientUrl.Parse(e.GetString()) : null;

    /// <summary>The first parseable URL of an array of strings, or a single URL string.</summary>
    public static Uri? LenientFirstUrl(this JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } array)
        {
            return element.LenientUrl();
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Policies.LenientUrl.Parse(item.GetString()) is { } url)
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>Provider ratings as 0–10: <c>rating</c> if in (0, 10], else <c>rating_5based</c> × 2 if in (0, 5].</summary>
    public static double? TenPointRating(this JsonElement element)
    {
        if (element.Field("rating").LenientDouble() is { } ten && ten > 0 && ten <= 10)
        {
            return ten;
        }

        return element.Field("rating_5based").LenientDouble() is { } five && five > 0 && five <= 5 ? five * 2 : null;
    }

    private static long? Truncate(double value) =>
        double.IsFinite(value) && value > long.MinValue && value < long.MaxValue ? (long)Math.Truncate(value) : null;

    private static long? ParseWhole(string? text)
    {
        var trimmed = text?.Trim(' ');
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i)
            ? i
            : ParseDouble(trimmed) is { } d ? Truncate(d) : null;
    }

    private static double? ParseDouble(string? text)
    {
        var trimmed = text?.Trim(' ');
        return !string.IsNullOrEmpty(trimmed)
            && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && double.IsFinite(d)
                ? d
                : null;
    }
}
