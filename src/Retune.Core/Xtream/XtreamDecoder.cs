using System.Globalization;
using System.Text;
using System.Text.Json;
using Retune.Core.Errors;
using Retune.Core.Models;

namespace Retune.Core.Xtream;

/// <summary>
/// Turns Xtream panel responses into models. Bad rows are skipped, never fatal; an empty body is an
/// error, never an empty list (it would overwrite a good cache). Spec: docs/spec/core.md §2.5–2.6.
/// </summary>
internal static class XtreamDecoder
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 64, AllowTrailingCommas = true };
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // ---- lists ---------------------------------------------------------------------------------

    public static IReadOnlyList<MediaCategory> Categories(ReadOnlyMemory<byte> body) => List(body, Category);

    public static IReadOnlyList<Channel> LiveStreams(ReadOnlyMemory<byte> body) => List(body, Channel);

    public static IReadOnlyList<Movie> VodStreams(ReadOnlyMemory<byte> body) => List(body, Movie);

    public static IReadOnlyList<Series> SeriesList(ReadOnlyMemory<byte> body) => List(body, Series);

    // ---- objects -------------------------------------------------------------------------------

    public static MediaInfo VodInfo(ReadOnlyMemory<byte> body)
    {
        using var doc = ParseObject(body);
        return doc.RootElement.Field("info") is { ValueKind: JsonValueKind.Object } info ? Info(info) : MediaInfo.Empty;
    }

    public static SeriesDetail SeriesInfo(ReadOnlyMemory<byte> body)
    {
        using var doc = ParseObject(body);
        var root = doc.RootElement;
        var info = root.Field("info") is { ValueKind: JsonValueKind.Object } i ? Info(i) : MediaInfo.Empty;
        return new SeriesDetail(info, Seasons(SeasonMetadata(root.Field("seasons")), EpisodeGroups(root.Field("episodes"))));
    }

    public static IReadOnlyList<EpgEntry> ShortEpg(ReadOnlyMemory<byte> body)
    {
        using var doc = ParseObject(body);
        if (doc.RootElement.Field("epg_listings") is not { ValueKind: JsonValueKind.Array } listings)
        {
            return [];
        }

        return Rows(listings, EpgRow).OrderBy(e => e.Start).ToList();
    }

    /// <summary>The account, or null when the panel rejects the login (<c>user_info.auth</c> ≠ 1).</summary>
    public static AccountInfo? Auth(ReadOnlyMemory<byte> body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body, Options);
        }
        catch (JsonException)
        {
            throw RetuneException.UnreadableResponse();
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw RetuneException.UnreadableResponse();
            }

            var user = doc.RootElement.Field("user_info");
            if (user is not { ValueKind: JsonValueKind.Object } u || u.Field("auth").LenientInt64() != 1)
            {
                return null;
            }

            return new AccountInfo(
                Status: u.Field("status").LenientText() ?? "Unknown",
                Message: u.Field("message").LenientText(),
                ExpiresAt: u.Field("exp_date").LenientDate(),
                IsTrial: u.Field("is_trial").LenientBool(),
                ActiveConnections: u.Field("active_cons").LenientInt(),
                MaxConnections: u.Field("max_connections").LenientInt(),
                AllowedFormats: StringArray(u.Field("allowed_output_formats")),
                ServerTimezone: doc.RootElement.Field("server_info") is { } server ? server.Field("timezone").LenientText() : null);
        }
    }

    // ---- rows ----------------------------------------------------------------------------------

    private static MediaCategory? Category(JsonElement e) =>
        e.Field("category_id").LenientText() is { } id
            ? new MediaCategory(id, e.Field("category_name").LenientText() ?? "Untitled")
            : null;

    private static Channel? Channel(JsonElement e) =>
        e.Field("stream_id").LenientText() is { } id
            ? new Channel(
                id,
                e.Field("num").LenientInt(),
                e.Field("name").LenientText() ?? $"Channel {id}",
                e.Field("stream_icon").LenientUrl(),
                e.Field("category_id").LenientText(),
                e.Field("epg_channel_id").LenientText(),
                DirectUrl: null,
                e.Field("tv_archive").LenientBool())
            : null;

    private static Movie? Movie(JsonElement e) =>
        e.Field("stream_id").LenientText() is { } id
            ? new Movie(
                id,
                e.Field("name").LenientText() ?? "Untitled",
                e.Field("stream_icon").LenientUrl(),
                e.Field("category_id").LenientText(),
                e.TenPointRating(),
                e.Field("added").LenientDate(),
                e.Field("container_extension").LenientText(),
                DirectUrl: null)
            : null;

    private static Series? Series(JsonElement e) =>
        e.Field("series_id").LenientText() is { } id
            ? new Series(
                id,
                e.Field("name").LenientText() ?? "Untitled",
                e.Field("cover").LenientUrl(),
                e.Field("backdrop_path").LenientFirstUrl(),
                e.Field("category_id").LenientText(),
                e.Field("plot").LenientText(),
                e.Field("genre").LenientText(),
                FirstText(e, "releaseDate", "release_date"),
                e.TenPointRating(),
                e.Field("last_modified").LenientDate())
            : null;

    private static MediaInfo Info(JsonElement e) => new(
        Plot: FirstText(e, "plot", "description"),
        Cast: FirstText(e, "cast", "actors"),
        Director: e.Field("director").LenientText(),
        Genre: e.Field("genre").LenientText(),
        ReleaseDate: FirstText(e, "releasedate", "releaseDate", "release_date"),
        DurationSeconds: e.Field("duration_secs").LenientInt() ?? MinutesToSeconds(e.Field("episode_run_time").LenientInt64()),
        Rating: e.TenPointRating(),
        Poster: e.Field("movie_image").LenientUrl() ?? e.Field("cover_big").LenientUrl() ?? e.Field("cover").LenientUrl(),
        Backdrop: e.Field("backdrop_path").LenientFirstUrl(),
        YoutubeTrailer: e.Field("youtube_trailer").LenientText());

    private static Episode? EpisodeRow(JsonElement e)
    {
        if (e.Field("id").LenientText() is not { } id)
        {
            return null;
        }

        var number = e.Field("episode_num").LenientInt() ?? 0;
        var info = e.Field("info") is { ValueKind: JsonValueKind.Object } i ? i : (JsonElement?)null;
        return new Episode(
            id,
            e.Field("season").LenientInt() ?? 0,
            number,
            e.Field("title").LenientText() ?? $"Episode {number}",
            e.Field("container_extension").LenientText(),
            info?.Field("plot").LenientText(),
            info?.Field("movie_image").LenientUrl(),
            info?.Field("duration_secs").LenientInt());
    }

    private static EpgEntry? EpgRow(JsonElement e)
    {
        var start = e.Field("start_timestamp").LenientDate();
        var end = e.Field("stop_timestamp").LenientDate();
        var title = e.Field("title").LenientString();
        if (start is null || end is null || title is null)
        {
            return null;
        }

        var summary = DecodeBase64Text(e.Field("description").LenientString());
        return new EpgEntry(DecodeBase64Text(title) ?? string.Empty, string.IsNullOrEmpty(summary) ? null : summary, start.Value, end.Value);
    }

    // ---- series assembly -----------------------------------------------------------------------

    private static Dictionary<int, (string? Name, Uri? Cover)> SeasonMetadata(JsonElement? seasons)
    {
        var result = new Dictionary<int, (string?, Uri?)>();
        if (seasons is not { ValueKind: JsonValueKind.Array } array)
        {
            return result;
        }

        foreach (var row in array.EnumerateArray())
        {
            if (row.Field("season_number").LenientInt() is { } number && !result.ContainsKey(number))
            {
                var cover = row.Field("cover_tmdb").LenientUrl() ?? row.Field("cover_big").LenientUrl() ?? row.Field("cover").LenientUrl();
                result[number] = (row.Field("name").LenientText(), cover);
            }
        }

        return result;
    }

    /// <summary>Accepts <c>{"1":[…],"2":[…]}</c> (usual) or <c>[[…],[…]]</c>; anything else gives no episodes.</summary>
    private static Dictionary<int, List<Episode>> EpisodeGroups(JsonElement? episodes)
    {
        var groups = new Dictionary<int, List<Episode>>();
        void Add(int season, List<Episode> list)
        {
            if (groups.TryGetValue(season, out var existing))
            {
                existing.AddRange(list);
            }
            else
            {
                groups[season] = list;
            }
        }

        switch (episodes)
        {
            case { ValueKind: JsonValueKind.Object } bySeason:
                foreach (var property in bySeason.EnumerateObject())
                {
                    var list = property.Value.ValueKind == JsonValueKind.Array ? Rows(property.Value, EpisodeRow).ToList() : [];
                    var season = int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                        ? n
                        : FirstDeclaredSeason(property.Value) ?? 0;
                    Add(season, list);
                }

                break;
            case { ValueKind: JsonValueKind.Array } nested:
                var index = 0;
                foreach (var inner in nested.EnumerateArray())
                {
                    var list = inner.ValueKind == JsonValueKind.Array ? Rows(inner, EpisodeRow).ToList() : [];
                    Add(FirstDeclaredSeason(inner) ?? index + 1, list);
                    index++;
                }

                break;
            default:
                break;
        }

        return groups;
    }

    /// <summary>The <c>season</c> field of the first decodable episode, if that episode declares one.</summary>
    private static int? FirstDeclaredSeason(JsonElement episodes)
    {
        if (episodes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var row in episodes.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object && row.Field("id").LenientText() is not null)
            {
                return row.Field("season").LenientInt();
            }
        }

        return null;
    }

    private static List<Season> Seasons(Dictionary<int, (string? Name, Uri? Cover)> metadata, Dictionary<int, List<Episode>> groups) =>
        groups
            .Where(g => g.Value.Count > 0)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                metadata.TryGetValue(g.Key, out var meta);
                var episodes = g.Value.Select(ep => ep with { Season = g.Key }).OrderBy(ep => ep.Number).ToList();
                return new Season(g.Key, meta.Name ?? $"Season {g.Key}", meta.Cover, episodes);
            })
            .ToList();

    // ---- plumbing ------------------------------------------------------------------------------

    private static List<T> List<T>(ReadOnlyMemory<byte> body, Func<JsonElement, T?> row)
        where T : class
    {
        if (body.IsEmpty)
        {
            throw RetuneException.EmptyResponse();
        }

        // Some panels answer an empty section with exactly "{}".
        if (body.Span.SequenceEqual("{}"u8))
        {
            return [];
        }

        using var doc = TryParse(body) ?? throw RetuneException.UnreadableResponse();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw RetuneException.UnreadableResponse();
        }

        var list = new List<T>(doc.RootElement.GetArrayLength());
        list.AddRange(Rows(doc.RootElement, row));
        return list;
    }

    /// <summary>Each row decodes on its own; one bad row never takes its neighbours down.</summary>
    private static IEnumerable<T> Rows<T>(JsonElement array, Func<JsonElement, T?> row)
        where T : class
    {
        foreach (var element in array.EnumerateArray())
        {
            T? item;
            try
            {
                item = element.ValueKind == JsonValueKind.Object ? row(element) : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
            {
                item = null;
            }

            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static JsonDocument ParseObject(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            throw RetuneException.EmptyResponse();
        }

        var doc = TryParse(body) ?? throw RetuneException.UnreadableResponse();
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            throw RetuneException.UnreadableResponse();
        }

        return doc;
    }

    private static JsonDocument? TryParse(ReadOnlyMemory<byte> body)
    {
        try
        {
            return JsonDocument.Parse(body, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstText(JsonElement e, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (e.Field(name).LenientText() is { } text)
            {
                return text;
            }
        }

        return null;
    }

    private static int? MinutesToSeconds(long? minutes) =>
        minutes is { } m && m is >= 0 and <= int.MaxValue / 60 ? (int)(m * 60) : null;

    private static List<string> StringArray(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Array } array)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return [];
            }

            list.Add(item.GetString()!);
        }

        return list;
    }

    /// <summary>EPG text is usually base64; use the decoded text only if it is valid UTF-8.</summary>
    private static string? DecodeBase64Text(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var buffer = new byte[raw.Length];
        if (Convert.TryFromBase64String(raw, buffer, out var written))
        {
            try
            {
                return StrictUtf8.GetString(buffer, 0, written).Trim();
            }
            catch (DecoderFallbackException)
            {
                // Not text: fall through to the raw value.
            }
        }

        return raw.Trim();
    }
}
