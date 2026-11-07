using Retune.Core.Models;
using Retune.Core.Policies;
using Retune.Core.Xtream;

namespace Retune.Core.Tests.Xtream;

public sealed class ServerAndUrlTests
{
    private static readonly XtreamUrls Urls = new(new XtreamCredentials("http://h.example:8080", "u", "p w"));

    [Theory]
    [InlineData("iptv.example:8080", "http://iptv.example:8080")]
    [InlineData("http://iptv.example:8080/", "http://iptv.example:8080")]
    [InlineData("https://host.example/player_api.php", "https://host.example")]
    [InlineData("http://host.example:80/get.php?username=a&password=b", "http://host.example:80")]
    [InlineData("  host.example  ", "http://host.example")]
    [InlineData("HTTPS://Host.example/panel/", "https://Host.example/panel")]
    [InlineData("http://h.example/xmltv.php#frag", "http://h.example")]
    [InlineData("http://h.example/sub///", "http://h.example/sub")]
    public void Normalizes_server_input(string input, string expected)
    {
        Assert.Equal(expected, XtreamServer.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a host")]
    [InlineData(null)]
    [InlineData("ftp://files.example")]
    [InlineData("http://")]
    public void Rejects_garbage_servers(string? input)
    {
        Assert.Null(XtreamServer.Normalize(input));
    }

    [Theory]
    [InlineData("http://iptv.example:8080", true)]
    [InlineData("HTTP://iptv.example", true)]
    [InlineData("host.example:8080", true)]
    [InlineData("http://h.example/get.php?username=a&password=b", true)]
    [InlineData("https://secure.example", false)]
    [InlineData("", false)]
    public void Flags_plain_http_addresses(string input, bool expected)
    {
        Assert.Equal(expected, XtreamServer.UsesPlainHttp(input));
    }

    [Fact]
    public void Extracts_credentials_from_a_provider_link()
    {
        var creds = XtreamServer.ParseLink("http://h.example:8080/get.php?username=viewer&password=12&type=m3u_plus&output=ts");

        Assert.NotNull(creds);
        Assert.Equal("http://h.example:8080", creds.Server);
        Assert.Equal("viewer", creds.Username);
        Assert.Equal("12", creds.Password);
    }

    [Theory]
    [InlineData("http://h.example:8080/list.m3u")]
    [InlineData("http://h.example/get.php?username=viewer")]
    [InlineData("http://h.example/get.php?username=&password=x")]
    public void Links_without_both_credentials_give_nothing(string link)
    {
        Assert.Null(XtreamServer.ParseLink(link));
    }

    [Fact]
    public void Link_credentials_are_percent_decoded_but_plus_is_literal()
    {
        var creds = XtreamServer.ParseLink("http://h.example/get.php?username=a%40b&password=x+y%2Fz");

        Assert.Equal("a@b", creds!.Username);
        Assert.Equal("x+y/z", creds.Password);
    }

    [Fact]
    public void Credentials_never_print_the_password()
    {
        var text = new XtreamCredentials("http://h", "user", "hunter2").ToString();

        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.Contains("user", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_stream_urls()
    {
        Assert.Equal("http://h.example:8080/live/u/p%20w/42.ts", Urls.Live("42", "ts"));
        Assert.Equal("http://h.example:8080/movie/u/p%20w/7.mkv", Urls.Movie(new Movie("7", "M", null, null, null, null, "mkv", null)));
        Assert.Equal("http://h.example:8080/series/u/p%20w/9.mp4", Urls.Episode(new Episode("9", 1, 1, "E")));
    }

    [Fact]
    public void Direct_urls_win_over_built_ones()
    {
        var movie = new Movie("7", "M", null, null, null, null, "mkv", "http://cdn.example/m.mkv");

        Assert.Equal("http://cdn.example/m.mkv", Urls.Movie(movie));
    }

    [Fact]
    public void Builds_api_urls_in_the_documented_parameter_order()
    {
        var url = Urls.Api("get_series_info", new Dictionary<string, string> { ["series_id"] = "5" });

        Assert.Equal("http://h.example:8080/player_api.php?username=u&password=p%20w&action=get_series_info&series_id=5", url);
    }

    [Fact]
    public void Api_and_stream_urls_agree_on_a_trailing_slash()
    {
        var urls = new XtreamUrls(new XtreamCredentials("http://h:8080/", "u", "p"));

        Assert.StartsWith("http://h:8080/player_api.php?", urls.Api(), StringComparison.Ordinal);
        Assert.StartsWith("http://h:8080/live/", urls.Live("1", "ts"), StringComparison.Ordinal);
    }

    [Fact]
    public void Extra_api_parameters_are_sorted_by_key()
    {
        var url = Urls.Api("get_short_epg", new Dictionary<string, string> { ["stream_id"] = "9", ["limit"] = "4" });

        Assert.EndsWith("&action=get_short_epg&limit=4&stream_id=9", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Plus_in_password_survives_php_query_parsing()
    {
        var url = new XtreamUrls(new XtreamCredentials("http://h", "u", "ab+c&d=e")).Api();

        Assert.Contains("password=ab%2Bc%26d%3De", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Slash_in_credentials_is_escaped_in_stream_urls()
    {
        var urls = new XtreamUrls(new XtreamCredentials("http://h.example:8080", "u/1", "a/b"));

        Assert.Equal("http://h.example:8080/live/u%2F1/a%2Fb/7.ts", urls.Live("7", "ts"));
    }

    [Fact]
    public void Path_encoding_keeps_sub_delims_and_encodes_the_rest()
    {
        Assert.Equal("a+b&c=d@e:f!", PercentEncoding.PathSegment("a+b&c=d@e:f!"));
        Assert.Equal("%20%2F%3F%23%25", PercentEncoding.PathSegment(" /?#%"));
        Assert.Equal("caf%C3%A9%F0%9F%98%80", PercentEncoding.PathSegment("café😀"));
    }

    [Fact]
    public void Query_encoding_leaves_only_unreserved_ascii_literal()
    {
        Assert.Equal("Az09-._~", PercentEncoding.QueryValue("Az09-._~"));
        Assert.Equal("%2B%26%3D%2F%20%C3%A9", PercentEncoding.QueryValue("+&=/ é"));
    }

    [Theory]
    [InlineData(LiveFormat.Ts, new string[0], "ts")]
    [InlineData(LiveFormat.Hls, new[] { "ts" }, "m3u8")]
    [InlineData(LiveFormat.Automatic, new string[0], "ts")]
    [InlineData(LiveFormat.Automatic, new[] { "m3u8", "ts" }, "ts")]
    [InlineData(LiveFormat.Automatic, new[] { "m3u8" }, "m3u8")]
    public void Resolves_the_live_format(LiveFormat preference, string[] allowed, string expected)
    {
        Assert.Equal(expected, LiveFormats.Extension(preference, allowed));
    }
}
