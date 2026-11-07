using System.Globalization;

namespace Retune.FakeProvider;

/// <summary>Size and login of the fake account. Defaults suit demos and screenshots; tests scale it up to 88k items.</summary>
public sealed record FakeProviderOptions(
    int Channels = 120,
    int Movies = 400,
    int Series = 80,
    string Username = "demo",
    string Password = "demo",
    string? MediaFolder = null);

/// <summary>
/// Deterministic, entirely made-up catalog content (no real channels, titles or brands), generated from word
/// lists so every run produces the same data.
/// </summary>
internal sealed class DemoCatalog
{
    private static readonly string[] LiveCategories = ["News", "Sports", "Kids", "Documentary", "Music", "Movies 24/7", "Lifestyle", "World"];
    private static readonly string[] VodCategories = ["Action", "Drama", "Comedy", "Sci-Fi", "Thriller", "Animation", "Romance", "Family"];
    private static readonly string[] SeriesCategories = ["Crime", "Fantasy", "Sitcom", "Mystery", "Reality"];
    private static readonly string[] ChannelWords = ["Nova", "Vertex", "Harbor", "Summit", "Lumen", "Atlas", "Cobalt", "Ember", "Meridian", "Quartz", "Willow", "Zenith"];
    private static readonly string[] ChannelSuffix = ["News", "Sports", "Kids", "Docs", "Hits", "Cinema", "Life", "One", "Plus", "World"];
    private static readonly string[] TitleA = ["The Silent", "Midnight", "Crimson", "Last", "Hidden", "Electric", "Golden", "Broken", "Northern", "Paper", "Glass", "Wild"];
    private static readonly string[] TitleB = ["Harbor", "Signal", "Orchard", "Frontier", "Garden", "Machine", "Promise", "Horizon", "Lantern", "Kingdom", "River", "Echo"];

    public DemoCatalog(FakeProviderOptions options)
    {
        Options = options;
    }

    public FakeProviderOptions Options { get; }

    public static IEnumerable<object> Categories(string kind) =>
        CategoryNames(kind).Select((name, i) => new { category_id = Id(kind, i), category_name = name, parent_id = 0 });

    public IEnumerable<object> LiveStreams() => Enumerable.Range(0, Options.Channels).Select(i => new
    {
        num = i + 1,
        name = $"{ChannelWords[i % ChannelWords.Length]} {ChannelSuffix[i / ChannelWords.Length % ChannelSuffix.Length]}{(i >= 120 ? $" {i}" : string.Empty)}",
        stream_type = "live",
        stream_id = 1000 + i,
        stream_icon = $"/art/logo/{i}.png",
        epg_channel_id = $"ch{i}.demo",
        category_id = Id("live", i % LiveCategories.Length),
        tv_archive = i % 7 == 0 ? 1 : 0,
    });

    public IEnumerable<object> VodStreams() => Enumerable.Range(0, Options.Movies).Select(i => new
    {
        num = i + 1,
        name = $"{Title(i)} ({2000 + (i % 25)})",
        stream_type = "movie",
        stream_id = 5000 + i,
        stream_icon = $"/art/poster/{i}.png",
        rating = ((i * 37 % 60) / 10.0 + 4).ToString("0.0", CultureInfo.InvariantCulture),
        added = (1_700_000_000 - (i * 3600)).ToString(CultureInfo.InvariantCulture),
        category_id = Id("vod", i % VodCategories.Length),
        container_extension = i % 3 == 0 ? "mkv" : "mp4",
    });

    public IEnumerable<object> SeriesList() => Enumerable.Range(0, Options.Series).Select(i => new
    {
        num = i + 1,
        name = Title(i + 7),
        series_id = 9000 + i,
        cover = $"/art/poster/s{i}.png",
        backdrop_path = new[] { $"/art/backdrop/s{i}.png" },
        plot = "A made-up story for demos and tests.",
        genre = SeriesCategories[i % SeriesCategories.Length],
        releaseDate = $"{2010 + (i % 15)}-0{1 + (i % 9)}-15",
        rating = ((i * 13 % 50) / 10.0 + 5).ToString("0.0", CultureInfo.InvariantCulture),
        last_modified = (1_700_000_000 - (i * 7200)).ToString(CultureInfo.InvariantCulture),
        category_id = Id("series", i % SeriesCategories.Length),
    });

    public static object SeriesInfo(int seriesId)
    {
        var i = seriesId - 9000;
        var seasons = 1 + (i % 3);
        var episodes = Enumerable.Range(1, seasons).ToDictionary(
            s => s.ToString(CultureInfo.InvariantCulture),
            s => Enumerable.Range(1, 6).Select(e => new
            {
                id = $"{seriesId}{s:00}{e:00}",
                episode_num = e,
                season = s,
                title = $"Chapter {e}",
                container_extension = "mp4",
                info = new { plot = "Things happen, then other things happen.", duration_secs = 2700, movie_image = $"/art/still/{seriesId}-{s}-{e}.png" },
            }).ToArray());
        return new
        {
            info = new { name = Title(i + 7), plot = "A made-up story for demos and tests.", cast = "A. Actor, B. Performer", director = "C. Director", genre = "Drama", releasedate = "2021-03-04", episode_run_time = "45", rating_5based = 4.1, cover = $"/art/poster/s{i}.png" },
            seasons = Enumerable.Range(1, seasons).Select(s => new { season_number = s, name = $"Season {s}", cover = $"/art/poster/s{i}.png" }),
            episodes,
        };
    }

    public static object VodInfo(int vodId) => new
    {
        info = new
        {
            plot = "A made-up film for demos and tests. Nothing here is real.",
            cast = "A. Actor, B. Performer",
            director = "C. Director",
            genre = VodCategories[(vodId - 5000) % VodCategories.Length],
            releasedate = $"{2000 + ((vodId - 5000) % 25)}-06-20",
            duration_secs = 6163,
            rating = "7.4",
            movie_image = $"/art/poster/{vodId - 5000}.png",
            backdrop_path = new[] { $"/art/backdrop/{vodId - 5000}.png" },
        },
        movie_data = new { stream_id = vodId },
    };

    public static object ShortEpg(int streamId, DateTimeOffset now)
    {
        var start = now.AddMinutes(-(now.Minute % 30)).AddSeconds(-now.Second);
        return new
        {
            epg_listings = Enumerable.Range(0, 4).Select(k => new
            {
                title = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Title(streamId + k)}")),
                description = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Made-up programme.")),
                start_timestamp = start.AddMinutes(30 * k).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                stop_timestamp = start.AddMinutes(30 * (k + 1)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            }),
        };
    }

    public string M3u(string baseUrl)
    {
        var lines = new List<string> { "#EXTM3U" };
        foreach (var (channel, i) in LiveStreams().Cast<dynamic>().Select((c, i) => (c, i)))
        {
            lines.Add($"#EXTINF:-1 tvg-id=\"ch{i}.demo\" tvg-logo=\"{baseUrl}/art/logo/{i}.png\" group-title=\"{LiveCategories[i % LiveCategories.Length]}\",{channel.name}");
            lines.Add($"{baseUrl}/live/{Options.Username}/{Options.Password}/{1000 + i}.ts");
        }

        return string.Join('\n', lines);
    }

    private static string Title(int i) => $"{TitleA[i % TitleA.Length]} {TitleB[(i / TitleA.Length + i) % TitleB.Length]}";

    private static string Id(string kind, int i) => $"{kind[0]}{i + 1}";

    private static string[] CategoryNames(string kind) => kind switch
    {
        "live" => LiveCategories,
        "vod" => VodCategories,
        _ => SeriesCategories,
    };
}
