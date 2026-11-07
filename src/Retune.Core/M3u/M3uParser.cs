using System.Security.Cryptography;
using System.Text;
using Retune.Core.Errors;
using Retune.Core.Models;
using Retune.Core.Policies;

namespace Retune.Core.M3u;

/// <summary>One playable line of an M3U playlist with its #EXTINF metadata.</summary>
public sealed record M3uEntry(string Url, string Title, IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// Parses M3U / M3U8 playlists. Every URL line passes the stream URL policy; everything else is
/// dropped silently. Spec: docs/spec/core.md §3.
/// </summary>
public static class M3uParser
{
    private const string Uncategorized = "Uncategorized";
    private const string GroupTitle = "group-title";
    private const string TvgName = "tvg-name";
    private const string TvgLogo = "tvg-logo";
    private const string TvgId = "tvg-id";

    private static readonly string[] OnDemandExtensions = ["mkv", "mp4", "avi", "mov", "m4v", "wmv"];
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>UTF-8 (BOM tolerated), falling back to ISO-8859-1, which can decode any byte sequence.</summary>
    public static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        if (bytes.StartsWith(utf8Bom))
        {
            bytes = bytes[utf8Bom.Length..];
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static IReadOnlyList<M3uEntry> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<M3uEntry>();
        (string Title, Dictionary<string, string> Attributes)? pendingInfo = null;
        string? pendingGroup = null;

        foreach (var raw in Lines(text))
        {
            var line = raw.Trim(' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.Ordinal))
            {
                var (header, title) = SplitTitle(line);
                pendingInfo = (title, Attributes(header));
            }
            else if (line.StartsWith("#EXTGRP:", StringComparison.Ordinal))
            {
                pendingGroup = line["#EXTGRP:".Length..].Trim(' ', '\t');
            }
            else if (line.StartsWith('#'))
            {
                // #EXTM3U, #EXTVLCOPT, … are ignored; external XMLTV EPG is not supported.
            }
            else if (UrlPolicy.IsAllowedStream(line))
            {
                var attributes = pendingInfo?.Attributes ?? [];
                if (!attributes.ContainsKey(GroupTitle) && pendingGroup is not null)
                {
                    attributes[GroupTitle] = pendingGroup;
                }

                entries.Add(new M3uEntry(line, ChooseTitle(pendingInfo?.Title, attributes, line), attributes));
                pendingInfo = null;
                pendingGroup = null;
            }

            // A rejected URL line keeps the pending info for the next valid line (parity with the original).
        }

        return entries;
    }

    /// <summary>Builds the catalog: dedupes by URL, splits live from on-demand, keeps first-seen category order.</summary>
    /// <exception cref="RetuneException">With <see cref="ProblemKind.EmptyPlaylist"/> when nothing is playable.</exception>
    public static Catalog BuildCatalog(IReadOnlyList<M3uEntry> entries, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var liveCategories = new OrderedCategories();
        var vodCategories = new OrderedCategories();
        var channels = new List<Channel>();
        var movies = new List<Movie>();

        foreach (var entry in entries)
        {
            if (!seenUrls.Add(entry.Url))
            {
                continue;
            }

            var id = StableId(entry.Url);
            var group = entry.Attributes.TryGetValue(GroupTitle, out var g) && g.Length > 0 ? g : Uncategorized;
            var logo = entry.Attributes.TryGetValue(TvgLogo, out var l) ? LenientUrl.Parse(l) : null;
            var (path, extension) = PathAndExtension(entry.Url);

            if (IsOnDemand(path, extension))
            {
                vodCategories.Add(group);
                movies.Add(new Movie(id, entry.Title, logo, group, Rating: null, Added: null, extension, entry.Url));
            }
            else
            {
                liveCategories.Add(group);
                channels.Add(new Channel(id, channels.Count + 1, entry.Title, logo, group,
                    entry.Attributes.GetValueOrDefault(TvgId), entry.Url, HasArchive: false));
            }
        }

        if (channels.Count == 0 && movies.Count == 0)
        {
            throw RetuneException.EmptyPlaylist();
        }

        return new Catalog(fetchedAt, liveCategories.ToList(), vodCategories.ToList(), [], channels, movies, []);
    }

    /// <summary>Lowercase hex of the first 16 bytes of SHA-256(url): stable, and never contains the URL's secrets.</summary>
    public static string StableId(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(url), hash);
        return Convert.ToHexStringLower(hash[..16]);
    }

    /// <summary>
    /// Splits <c>#EXTINF:-1 a="x, y",Title, with comma</c> at the first comma outside quotes.
    /// Without such a comma the title is empty and the whole line is the header.
    /// </summary>
    internal static (string Header, string Title) SplitTitle(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            switch (line[i])
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ',' when !inQuotes:
                    return (line[..i], line[(i + 1)..].Trim(' ', '\t'));
                default:
                    break;
            }
        }

        return (line, string.Empty);
    }

    /// <summary>
    /// Equivalent to the regex <c>([A-Za-z0-9_-]+)="([^"]*)"</c> applied left to right, in one linear pass
    /// (a backtracking regex can hang on hostile 500k-character lines). Keys are lowercased; later keys win.
    /// </summary>
    internal static Dictionary<string, string> Attributes(string header)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position < header.Length)
        {
            var equalsQuote = header.IndexOf("=\"", position, StringComparison.Ordinal);
            if (equalsQuote < 0)
            {
                break;
            }

            var keyStart = equalsQuote;
            while (keyStart > position && IsKeyChar(header[keyStart - 1]))
            {
                keyStart--;
            }

            if (keyStart == equalsQuote)
            {
                position = equalsQuote + 1; // `="` with no key in front: not an attribute
                continue;
            }

            var valueStart = equalsQuote + 2;
            var valueEnd = header.IndexOf('"', valueStart);
            if (valueEnd < 0)
            {
                break; // unclosed quote: nothing after it can match either
            }

            result[header[keyStart..equalsQuote].ToLowerInvariant()] = header[valueStart..valueEnd];
            position = valueEnd + 1;
        }

        return result;
    }

    private static bool IsKeyChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    /// <summary>
    /// The EXTINF title, then tvg-name, then the URL's file name. The original never reached the fallbacks,
    /// so blank titles produced blank names (spec Q5, improved here as agreed in PLAN.md).
    /// </summary>
    private static string ChooseTitle(string? extinfTitle, Dictionary<string, string> attributes, string url)
    {
        if (!string.IsNullOrWhiteSpace(extinfTitle))
        {
            return extinfTitle;
        }

        if (attributes.TryGetValue(TvgName, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        var (path, _) = PathAndExtension(url);
        var file = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..])).Trim();
        return file.Length > 0 ? file : Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }

    private static (string Path, string? Extension) PathAndExtension(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (string.Empty, null);
        }

        var path = uri.AbsolutePath;
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];
        var dot = lastSegment.LastIndexOf('.');
        var extension = dot >= 0 && dot < lastSegment.Length - 1 ? lastSegment[(dot + 1)..] : null;
        return (path, extension);
    }

    private static bool IsOnDemand(string path, string? extension)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains("/movie/", StringComparison.Ordinal)
            || lower.Contains("/series/", StringComparison.Ordinal)
            || (extension is not null && OnDemandExtensions.Contains(extension.ToLowerInvariant(), StringComparer.Ordinal));
    }

    /// <summary>Splits on LF, CR, CRLF, NEL, LS and PS (vertical tab and form feed too).</summary>
    private static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\n' or '\r' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029')
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    private sealed class OrderedCategories
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<MediaCategory> _list = [];

        public void Add(string group)
        {
            if (_seen.Add(group))
            {
                _list.Add(new MediaCategory(group, group));
            }
        }

        public List<MediaCategory> ToList() => _list;
    }
}
