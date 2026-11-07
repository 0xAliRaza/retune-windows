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

    public IEnumerable<object> LiveStreams(string baseUrl) => Enumerable.Range(0, Options.Channels).Select(i => new
    {
        num = i + 1,
        name = $"{ChannelWords[i % ChannelWords.Length]} {ChannelSuffix[i / ChannelWords.Length % ChannelSuffix.Length]}{(i >= 120 ? $" {i}" : string.Empty)}",
        stream_type = "live",
        stream_id = 1000 + i,
        stream_icon = $"{baseUrl}/art/logo/{i}.png",
        epg_channel_id = $"ch{i}.demo",
        category_id = Id("live", i % LiveCategories.Length),
        tv_archive = i % 7 == 0 ? 1 : 0,
    });

    public IEnumerable<object> VodStreams(string baseUrl) => Enumerable.Range(0, Options.Movies).Select(i => new
    {
        num = i + 1,
        name = $"{Title(i)} ({2000 + (i % 25)})",
        stream_type = "movie",
        stream_id = 5000 + i,
        stream_icon = $"{baseUrl}/art/poster/{i}.png",
        rating = ((i * 37 % 60) / 10.0 + 4).ToString("0.0", CultureInfo.InvariantCulture),
        added = (1_700_000_000 - (i * 3600)).ToString(CultureInfo.InvariantCulture),
        category_id = Id("vod", i % VodCategories.Length),
        container_extension = i % 3 == 0 ? "mkv" : "mp4",
    });

    public IEnumerable<object> SeriesList(string baseUrl) => Enumerable.Range(0, Options.Series).Select(i => new
    {
        num = i + 1,
        name = Title(i + 7),
        series_id = 9000 + i,
        cover = $"{baseUrl}/art/poster/s{i}.png",
        backdrop_path = new[] { $"{baseUrl}/art/backdrop/s{i}.png" },
        plot = Plot(i + 3),
        genre = SeriesCategories[i % SeriesCategories.Length],
        releaseDate = $"{2010 + (i % 15)}-0{1 + (i % 9)}-15",
        rating = ((i * 13 % 50) / 10.0 + 5).ToString("0.0", CultureInfo.InvariantCulture),
        last_modified = (1_700_000_000 - (i * 7200)).ToString(CultureInfo.InvariantCulture),
        category_id = Id("series", i % SeriesCategories.Length),
    });

    public static object SeriesInfo(int seriesId, string baseUrl)
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
                info = new { plot = EpisodePlot((s * 31) + e), duration_secs = 2700, movie_image = $"{baseUrl}/art/still/{seriesId}-{s}-{e}.png" },
            }).ToArray());
        return new
        {
            info = new { name = Title(i + 7), plot = Plot(i + 3), cast = "A. Actor, B. Performer", director = "C. Director", genre = "Drama", releasedate = "2021-03-04", episode_run_time = "45", rating_5based = 4.1, cover = $"{baseUrl}/art/poster/s{i}.png", backdrop_path = new[] { $"{baseUrl}/art/backdrop/s{i}.png" } },
            seasons = Enumerable.Range(1, seasons).Select(s => new { season_number = s, name = $"Season {s}", cover = $"{baseUrl}/art/poster/s{i}.png" }),
            episodes,
        };
    }

    public static object VodInfo(int vodId, string baseUrl) => new
    {
        info = new
        {
            plot = Plot(vodId),
            cast = "A. Actor, B. Performer",
            director = "C. Director",
            genre = VodCategories[(vodId - 5000) % VodCategories.Length],
            releasedate = $"{2000 + ((vodId - 5000) % 25)}-06-20",
            duration_secs = 6163,
            rating = "7.4",
            movie_image = $"{baseUrl}/art/poster/{vodId - 5000}.png",
            backdrop_path = new[] { $"{baseUrl}/art/backdrop/{vodId - 5000}.png" },
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
        foreach (var (channel, i) in LiveStreams(baseUrl).Cast<dynamic>().Select((c, i) => (c, i)))
        {
            lines.Add($"#EXTINF:-1 tvg-id=\"ch{i}.demo\" tvg-logo=\"{baseUrl}/art/logo/{i}.png\" group-title=\"{LiveCategories[i % LiveCategories.Length]}\",{channel.name}");
            lines.Add($"{baseUrl}/live/{Options.Username}/{Options.Password}/{1000 + i}.ts");
        }

        return string.Join('\n', lines);
    }

    // Invented synopses for demo screenshots; nothing here refers to a real work.
    private static readonly string[] Plots =
    [
        "A lighthouse keeper finds a logbook that predicts tomorrow's storms, and one entry that predicts her own.",
        "Two rival cartographers race across an uncharted valley, each hiding why the map matters so much.",
        "When the city's power fails for a week, a night-shift radio host becomes the only voice anyone trusts.",
        "A retired composer agrees to teach one last student, who seems to already know every unfinished piece.",
        "An orchard that blooms in winter draws a botanist, a con artist and a family with something to hide.",
        "A courier who never opens a parcel is handed one addressed to himself, ten years in the future.",
        "On the last train across the frontier, strangers discover they all bought tickets for the same reason.",
        "A small-town baker enters a national contest and accidentally exposes the judges' oldest secret.",
        "Deep in a research station, the night crew starts receiving messages from the day crew who left.",
        "A painter restoring a church mural uncovers a second picture underneath, and it is still changing.",
        "Three sisters reopen their late father's cinema and find the films in the vault have no credits.",
        "A detective with no memory of the last year must solve the case he apparently closed.",
    ];

    private static readonly string[] EpisodePlots =
    [
        "An unexpected visitor arrives with a warning nobody wants to hear.",
        "Old allies meet again, and only one of them is telling the truth.",
        "A plan comes together, until the one detail nobody checked.",
        "The storm cuts the town off, and tempers run short.",
        "A letter from the past changes what everyone thought they knew.",
        "Everything rides on a single night, and a single choice.",
    ];

    private static string Plot(int i) => Plots[i % Plots.Length];

    private static string EpisodePlot(int i) => EpisodePlots[i % EpisodePlots.Length];

    private static string Title(int i) => $"{TitleA[i % TitleA.Length]} {TitleB[(i / TitleA.Length + i) % TitleB.Length]}";

    private static string Id(string kind, int i) => $"{kind[0]}{i + 1}";

    private static string[] CategoryNames(string kind) => kind switch
    {
        "live" => LiveCategories,
        "vod" => VodCategories,
        _ => SeriesCategories,
    };
}
