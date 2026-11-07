using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Retune.Core.Errors;
using Retune.Core.M3u;
using Retune.Core.Xtream;

namespace Retune.Core.Tests.M3u;

public sealed class M3uParserTests
{
    private const string Sample = """
        #EXTM3U url-tvg="http://epg.example/xmltv.php"
        #EXTINF:-1 tvg-id="bbc1.uk" tvg-name="BBC One" tvg-logo="http://logo/bbc.png" group-title="UK, Entertainment",BBC One HD
        http://h.example:8080/live/u/p/1.ts
        #EXTINF:-1 tvg-logo="http://logo/m.jpg" group-title="Movies",Some Movie (2024)
        http://h.example:8080/movie/u/p/77.mkv
        #EXTINF:-1,No Attributes Channel
        #EXTGRP:Misc
        https://cdn.example/stream.m3u8
        #EXTINF:-1 group-title="Docs",Local File
        http://cdn.example/files/doc.mp4
        """;

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void Parses_entries_attributes_and_titles()
    {
        var entries = M3uParser.Parse(Sample);

        Assert.Equal(4, entries.Count);
        Assert.Equal("BBC One HD", entries[0].Title);
        Assert.Equal("UK, Entertainment", entries[0].Attributes["group-title"]);
        Assert.Equal("bbc1.uk", entries[0].Attributes["tvg-id"]);
        Assert.Equal("http://logo/bbc.png", entries[0].Attributes["tvg-logo"]);
        Assert.Equal("Misc", entries[2].Attributes["group-title"]);
    }

    [Fact]
    public void Splits_live_and_on_demand()
    {
        var catalog = M3uParser.BuildCatalog(M3uParser.Parse(Sample), Now);

        Assert.Equal(["BBC One HD", "No Attributes Channel"], catalog.Channels.Select(c => c.Name));
        Assert.Equal(["UK, Entertainment", "Misc"], catalog.LiveCategories.Select(c => c.Name));
        Assert.Equal(["Some Movie (2024)", "Local File"], catalog.Movies.Select(m => m.Name));
        Assert.Equal(["Movies", "Docs"], catalog.VodCategories.Select(c => c.Name));
        Assert.Empty(catalog.SeriesCategories);
        Assert.Empty(catalog.Series);
        Assert.Equal("mkv", catalog.Movies[0].ContainerExtension);
        Assert.Equal("http://h.example:8080/live/u/p/1.ts", catalog.Channels[0].DirectUrl);
        Assert.Equal([1, 2], catalog.Channels.Select(c => c.Number));
        Assert.Equal("http://logo/m.jpg", catalog.Movies[0].Poster?.AbsoluteUri);
        Assert.Equal(Now, catalog.FetchedAt);
    }

    [Fact]
    public void Title_comma_inside_a_quoted_attribute_is_not_the_separator()
    {
        var entry = Assert.Single(M3uParser.Parse("#EXTINF:-1 tvg-name=\"A, B\" group-title=\"G\",Real, Title\nhttp://h/1.ts"));

        Assert.Equal("Real, Title", entry.Title);
        Assert.Equal("A, B", entry.Attributes["tvg-name"]);
    }

    [Fact]
    public void Duplicate_urls_are_dropped()
    {
        var catalog = M3uParser.BuildCatalog(M3uParser.Parse("#EXTINF:-1,One\nhttp://h/1.ts\n#EXTINF:-1,Two\nhttp://h/1.ts"), Now);

        Assert.Equal("One", Assert.Single(catalog.Channels).Name);
    }

    [Fact]
    public void Rejected_lines_are_dropped_silently_and_keep_the_pending_info()
    {
        var entries = M3uParser.Parse("""
            #EXTINF:-1,Carried Over
            file:///C:/secret.ts
            C:\local\x.ts
            javascript:alert(1)
            http://h/ok.ts
            """);

        Assert.Equal("Carried Over", Assert.Single(entries).Title);
    }

    [Fact]
    public void Explicitly_empty_group_title_blocks_extgrp_and_counts_as_uncategorized()
    {
        var catalog = M3uParser.BuildCatalog(M3uParser.Parse("#EXTINF:-1 group-title=\"\",X\n#EXTGRP:Ignored\nhttp://h/1.ts"), Now);

        Assert.Equal("Uncategorized", catalog.Channels[0].CategoryId);
    }

    [Theory]
    [InlineData("#EXTINF:-1 tvg-name=\"Named\",\nhttp://h/a/1.ts", "Named")]
    [InlineData("#EXTINF:-1,\nhttp://h/a/My%20Show.ts", "My Show")]
    [InlineData("http://h/plain.m3u8", "plain")]
    [InlineData("http://h/", "h")]
    public void Blank_titles_fall_back_to_tvg_name_then_the_file_name(string playlist, string expected)
    {
        Assert.Equal(expected, Assert.Single(M3uParser.Parse(playlist)).Title);
    }

    [Theory]
    [InlineData("http://h/series/x/1.mp4", true)]
    [InlineData("http://h/MOVIE/x/1", true)]
    [InlineData("http://h/vod/1.AVI", true)]
    [InlineData("http://h/live/1.ts", false)]
    [InlineData("http://h/live/1.m3u8", false)]
    public void On_demand_is_detected_by_path_or_extension(string url, bool onDemand)
    {
        var catalog = M3uParser.BuildCatalog([new M3uEntry(url, "X", new Dictionary<string, string>())], Now);

        Assert.Equal(onDemand, catalog.Movies.Count == 1);
    }

    [Fact]
    public void Ids_never_contain_the_stream_url_and_locators_have_no_secret()
    {
        const string url = "http://h.example/get.php?username=u&password=topsecret&stream=1.ts";
        var catalog = M3uParser.BuildCatalog([new M3uEntry(url, "X", new Dictionary<string, string>())], Now);
        var channel = catalog.Channels[0];

        Assert.Equal(32, channel.Id.Length);
        Assert.DoesNotContain("topsecret", channel.Id, StringComparison.Ordinal);
        Assert.DoesNotContain("topsecret", Locators.Opaque(channel.Id), StringComparison.Ordinal);
        Assert.Equal(url, channel.DirectUrl);
        Assert.Equal(M3uParser.StableId(url), channel.Id);
    }

    [Fact]
    public void A_playlist_with_nothing_playable_is_an_error()
    {
        var ex = Assert.Throws<RetuneException>(() => M3uParser.BuildCatalog(M3uParser.Parse("#EXTM3U\n#EXTINF:-1,X\nfile:///x.ts"), Now));

        Assert.Equal(ProblemKind.EmptyPlaylist, ex.Kind);
    }

    [Fact]
    public void Every_kind_of_line_break_splits_lines()
    {
        var entries = M3uParser.Parse("#EXTINF:-1,A\r\nhttp://h/a.ts\r#EXTINF:-1,B\u2028http://h/b.ts\u0085http://h/c.ts");

        Assert.Equal(["A", "B", "c"], entries.Select(e => e.Title));
    }

    [Fact]
    public void Text_decoding_falls_back_to_latin1()
    {
        Assert.Equal("Café", M3uParser.DecodeText(Encoding.UTF8.GetBytes("Café")));
        Assert.Equal("Café", M3uParser.DecodeText([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Café")]));
        Assert.Equal("Café", M3uParser.DecodeText(Encoding.Latin1.GetBytes("Café")));
    }
}

public sealed class AttributeScannerTests
{
    private static readonly Regex Reference = new("([A-Za-z0-9_-]+)=\"([^\"]*)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static Dictionary<string, string> Expected(string header)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Reference.Matches(header))
        {
            result[m.Groups[1].Value.ToLowerInvariant()] = m.Groups[2].Value;
        }

        return result;
    }

    [Theory]
    [InlineData("#EXTINF:-1 TVG-Logo=\"L\" tvg-id=\"i\"")]
    [InlineData("#EXTINF:-1 a=\"\" b=\"x\"")]
    [InlineData("#EXTINF:-1 a=\"1\" A=\"2\"")]
    [InlineData("#EXTINF:-1 a=\"unclosed b=\"x")]
    [InlineData("#EXTINF:-1 =\"nokey\" b=\"x\"")]
    [InlineData("#EXTINF:-1 a=unquoted b=\"x\"")]
    [InlineData("#EXTINF:-1 a = \"spaced\" b=\"x\"")]
    [InlineData("#EXTINF:-1 x-y_z9=\"v\"tail=\"w\"")]
    [InlineData("#EXTINF:-1 name=\"Zürich 東京 😀\"")]
    [InlineData("")]
    [InlineData("#EXTINF:-1 a=\"x\" \"stray\" b=\"y\" c=\"")]
    public void Matches_the_reference_regex_on_fixed_headers(string header)
    {
        Assert.Equal(Expected(header), M3uParser.Attributes(header));
    }

    [Fact]
    public void Matches_the_reference_regex_on_5000_generated_headers()
    {
        var random = new Random(20261107);
        const string alphabet = "ab-_9Z =\"\" ,=\"x";
        for (var n = 0; n < 5000; n++)
        {
            var chars = new char[random.Next(0, 60)];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[random.Next(alphabet.Length)];
            }

            var header = new string(chars);
            Assert.Equal(Expected(header), M3uParser.Attributes(header));
        }
    }

    [Fact]
    public void A_huge_line_parses_quickly()
    {
        var header = "#EXTINF:-1 " + string.Concat(Enumerable.Repeat("a=\"", 166_000)) + new string('x', 2_000);
        Assert.True(header.Length >= 500_000);

        var watch = Stopwatch.StartNew();
        M3uParser.Attributes(header);
        M3uParser.SplitTitle(header);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Took {watch.Elapsed}.");
    }
}
