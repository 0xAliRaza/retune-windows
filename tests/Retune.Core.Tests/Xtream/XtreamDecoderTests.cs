using System.Text;
using Retune.Core.Errors;
using Retune.Core.Xtream;

namespace Retune.Core.Tests.Xtream;

public sealed class XtreamDecoderTests
{
    private static ReadOnlyMemory<byte> Json(string text) => Encoding.UTF8.GetBytes(text);

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Live_streams_decode_with_nulls_and_mixed_types()
    {
        var channels = XtreamDecoder.LiveStreams(Json("""
            [
              {"stream_id": 42, "num": "3", "name": "BBC One", "stream_icon": "http:\/\/logo.example\/bbc.png",
               "category_id": 5, "epg_channel_id": null, "tv_archive": "1"},
              {"stream_id": "43", "name": "  ", "stream_icon": "", "tv_archive": 0},
              {"name": "No id, dropped"},
              "not an object",
              {"stream_id": null}
            ]
            """));

        Assert.Equal(2, channels.Count);
        var bbc = channels[0];
        Assert.Equal("42", bbc.Id);
        Assert.Equal(3, bbc.Number);
        Assert.Equal("http://logo.example/bbc.png", bbc.Logo?.AbsoluteUri);
        Assert.Equal("5", bbc.CategoryId);
        Assert.Null(bbc.EpgChannelId);
        Assert.True(bbc.HasArchive);
        Assert.Null(bbc.DirectUrl);

        Assert.Equal("Channel 43", channels[1].Name);
        Assert.Null(channels[1].Logo);
        Assert.False(channels[1].HasArchive);
    }

    [Fact]
    public void Vod_rating_accepts_string_int_and_float()
    {
        var movies = XtreamDecoder.VodStreams(Json("""
            [
              {"stream_id": 1, "rating": 7, "added": "1700000000"},
              {"stream_id": 2, "rating": "7.2", "rating_5based": 0},
              {"stream_id": 3, "rating": "0", "rating_5based": 4.1},
              {"stream_id": 4, "rating": "0", "rating_5based": "0", "container_extension": "mkv"},
              {"stream_id": 5, "rating": "", "rating_5based": "3.5"}
            ]
            """));

        Assert.Equal([7.0, 7.2, 8.2, null, 7.0], movies.Select(m => m.Rating).ToList());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), movies[0].Added);
        Assert.Equal("Untitled", movies[0].Name);
        Assert.Equal("mkv", movies[3].ContainerExtension);
    }

    [Fact]
    public void Series_takes_the_first_backdrop_from_an_array()
    {
        var series = XtreamDecoder.SeriesList(Json("""
            [{"series_id": 9, "name": "Show", "backdrop_path": ["", "http://img.example/b1.jpg", "http://img.example/b2.jpg"],
              "rating": "0", "releaseDate": "2025-06-20", "last_modified": 1700000000}]
            """));

        var show = Assert.Single(series);
        Assert.Equal("http://img.example/b1.jpg", show.Backdrop?.AbsoluteUri);
        Assert.Null(show.Rating);
        Assert.Equal("2025-06-20", show.ReleaseDate);
        Assert.Equal("2025", show.Year);
    }

    [Fact]
    public void Series_release_date_falls_back_to_snake_case()
    {
        var show = Assert.Single(XtreamDecoder.SeriesList(Json("""[{"series_id": 1, "releaseDate": "", "release_date": "2019-01-01"}]""")));

        Assert.Equal("2019-01-01", show.ReleaseDate);
    }

    [Fact]
    public void Series_info_groups_episodes_by_season_key()
    {
        var detail = XtreamDecoder.SeriesInfo(Json("""
            {
              "info": {"name": "Show", "plot": "Plot", "episode_run_time": "45", "releasedate": "2025-02-01", "rating_5based": 4},
              "seasons": [{"season_number": 1, "name": "The Beginning", "cover_tmdb": "http://img.example/s1.jpg"}],
              "episodes": {
                "2": [{"id": "21", "episode_num": 1, "season": 9, "title": "Two-One", "info": []}],
                "1": [
                  {"id": "12", "episode_num": 2, "title": "One-Two", "info": {"plot": "p", "movie_image": "http://img.example/e.jpg", "duration_secs": 1800}},
                  {"id": "11", "episode_num": 1, "title": "One-One"}
                ]
              }
            }
            """));

        Assert.Equal([1, 2], detail.Seasons.Select(s => s.Number));
        var first = detail.Seasons[0];
        Assert.Equal("The Beginning", first.Name);
        Assert.Equal("http://img.example/s1.jpg", first.Cover?.AbsoluteUri);
        Assert.Equal(["11", "12"], first.Episodes.Select(e => e.Id));
        Assert.Equal(1800, first.Episodes[1].DurationSeconds);
        Assert.Equal("Season 2", detail.Seasons[1].Name);
        Assert.Equal(2, detail.Seasons[1].Episodes[0].Season);
        Assert.Null(detail.Seasons[1].Episodes[0].Plot);
        Assert.Equal(2700, detail.Info.DurationSeconds);
        Assert.Equal("2025", detail.Info.Year);
        Assert.Equal(8.0, detail.Info.Rating);
    }

    [Fact]
    public void Series_info_accepts_episodes_as_nested_arrays()
    {
        var detail = XtreamDecoder.SeriesInfo(Json("""
            {"info": [], "episodes": [[{"id": "31", "episode_num": 1, "season": 3, "info": []}], [{"id": "x1", "episode_num": 1}]]}
            """));

        Assert.Equal([2, 3], detail.Seasons.Select(s => s.Number));
        Assert.Null(detail.Info.Plot);
        Assert.Equal("Episode 1", detail.Seasons[1].Episodes[0].Title);
    }

    [Fact]
    public void Series_info_with_unknown_episode_shape_has_no_seasons()
    {
        Assert.Empty(XtreamDecoder.SeriesInfo(Json("""{"episodes": "none"}""")).Seasons);
    }

    [Fact]
    public void Vod_info_tolerates_an_empty_info_array()
    {
        Assert.Equal(Models.MediaInfo.Empty, XtreamDecoder.VodInfo(Json("""{"info": [], "movie_data": {}}""")));

        var full = XtreamDecoder.VodInfo(Json("""
            {"info": {"description": "D", "actors": "A, B", "duration_secs": 6163, "rating": "6.5",
                      "cover_big": "http://img.example/p.jpg", "youtube_trailer": "abc"}}
            """));
        Assert.Equal(6163, full.DurationSeconds);
        Assert.Equal(6.5, full.Rating);
        Assert.Equal("http://img.example/p.jpg", full.Poster?.AbsoluteUri);
        Assert.Equal("D", full.Plot);
        Assert.Equal("A, B", full.Cast);
        Assert.Equal("abc", full.YoutubeTrailer);
    }

    [Fact]
    public void Epg_decodes_base64_and_sorts()
    {
        var epg = XtreamDecoder.ShortEpg(Json($$"""
            {"epg_listings": [
              {"title": "{{B64("Later Show")}}", "description": "{{B64("Desc")}}", "start_timestamp": "1700003600", "stop_timestamp": 1700007200},
              {"title": "{{B64("  News  ")}}", "description": "", "start_timestamp": 1700000000, "stop_timestamp": "1700003600"},
              {"title": "Plain text, not base64!", "start_timestamp": 1700007200, "stop_timestamp": 1700010800},
              {"title": "No times"}
            ]}
            """));

        Assert.Equal(["News", "Later Show", "Plain text, not base64!"], epg.Select(e => e.Title));
        Assert.Null(epg[0].Summary);
        Assert.Equal("Desc", epg[1].Summary);
        var mid = DateTimeOffset.FromUnixTimeSeconds(1_700_001_800);
        Assert.True(epg[0].IsLive(mid));
        Assert.Equal(0.5, epg[0].Progress(mid), 3);
    }

    [Fact]
    public void Auth_parses_the_account()
    {
        var account = XtreamDecoder.Auth(Json("""
            {"user_info": {"auth": 1, "status": "Active", "exp_date": "1893456000", "is_trial": "0",
                           "active_cons": "0", "max_connections": "1", "allowed_output_formats": ["m3u8", "ts"]},
             "server_info": {"timezone": "Europe/Zurich"}}
            """));

        Assert.NotNull(account);
        Assert.True(account.IsActive);
        Assert.Equal(1, account.MaxConnections);
        Assert.Equal(["m3u8", "ts"], account.Formats);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_893_456_000), account.ExpiresAt);
        Assert.False(account.IsTrial);
        Assert.Equal("Europe/Zurich", account.ServerTimezone);
    }

    [Theory]
    [InlineData("""{"user_info": {"auth": 0}}""")]
    [InlineData("""{"user_info": {}}""")]
    [InlineData("""{"server_info": {}}""")]
    public void Auth_rejects_a_bad_login(string body)
    {
        Assert.Null(XtreamDecoder.Auth(Json(body)));
    }

    [Fact]
    public void Auth_formats_must_be_a_string_array()
    {
        var account = XtreamDecoder.Auth(Json("""{"user_info": {"auth": "1", "allowed_output_formats": "m3u8,ts"}}"""));

        Assert.Empty(account!.Formats);
        Assert.Equal("Unknown", account.Status);
    }

    [Fact]
    public void Lenient_ints_reject_non_finite_and_huge_values_but_keep_the_rows()
    {
        var channels = XtreamDecoder.LiveStreams(Json("""
            [{"stream_id": 1, "num": "nan"}, {"stream_id": 2, "num": 1e300}, {"stream_id": 3, "num": "inf"}, {"stream_id": 4, "num": "12.9"}]
            """));

        Assert.Equal([null, null, null, 12], channels.Select(c => c.Number));
    }

    [Fact]
    public void Numbers_beyond_double_range_give_null_fields_not_dropped_rows()
    {
        var channels = XtreamDecoder.LiveStreams(Json("""[{"stream_id": 1, "num": 1e400}, {"stream_id": 2e400, "name": "Huge id"}]"""));

        Assert.Equal(2, channels.Count);
        Assert.Null(channels[0].Number);
        Assert.Equal("2e400", channels[1].Id);
    }

    [Fact]
    public void Runtime_overflow_does_not_trap()
    {
        var info = XtreamDecoder.VodInfo(Json("""{"info": {"episode_run_time": 9223372036854775807}}"""));

        Assert.Null(info.DurationSeconds);
    }

    [Fact]
    public void Absurd_dates_are_discarded()
    {
        var movies = XtreamDecoder.VodStreams(Json("""[{"stream_id": 1, "added": 1e21}, {"stream_id": 2, "added": 1700000000}, {"stream_id": 3, "added": "0"}]"""));

        Assert.Null(movies[0].Added);
        Assert.NotNull(movies[1].Added);
        Assert.Null(movies[2].Added);
    }

    [Fact]
    public void Categories_require_an_id_and_default_their_name()
    {
        var categories = XtreamDecoder.Categories(Json("""[{"category_id": 7, "category_name": " News "}, {"category_id": "8"}, {"category_name": "orphan"}]"""));

        Assert.Equal(["7", "8"], categories.Select(c => c.Id));
        Assert.Equal("News", categories[0].Name);
        Assert.Equal("Untitled", categories[1].Name);
    }

    [Fact]
    public void Empty_object_is_an_empty_list_but_an_empty_body_is_an_error()
    {
        Assert.Empty(XtreamDecoder.Categories(Json("{}")));
        Assert.Empty(XtreamDecoder.Categories(Json("[]")));
        Assert.Equal(ProblemKind.EmptyResponse, Assert.Throws<RetuneException>(() => XtreamDecoder.Categories(ReadOnlyMemory<byte>.Empty)).Kind);
        Assert.Equal(ProblemKind.UnreadableResponse, Assert.Throws<RetuneException>(() => XtreamDecoder.Categories(Json("""{"a":1}"""))).Kind);
        Assert.Equal(ProblemKind.UnreadableResponse, Assert.Throws<RetuneException>(() => XtreamDecoder.Categories(Json("<html>"))).Kind);
    }
}
