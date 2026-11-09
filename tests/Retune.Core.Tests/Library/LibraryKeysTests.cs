using Retune.Core.Library;
using Retune.Core.Models;

namespace Retune.Core.Tests.Library;

public sealed class LibraryKeysTests
{
    private static readonly Guid Playlist = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public void Keys_embed_the_upper_case_playlist_id()
    {
        Assert.Equal("0F8FAD5B-D9CB-469F-A165-70867728950E/", LibraryKeys.PlaylistPrefix(Playlist));
        Assert.Equal("0F8FAD5B-D9CB-469F-A165-70867728950E/movie/7", LibraryKeys.Content(Playlist, ContentKind.Movie, "7"));
        Assert.Equal("0F8FAD5B-D9CB-469F-A165-70867728950E/category/live/104", LibraryKeys.Category(Playlist, LibrarySection.Live, "104"));
        Assert.Equal("0F8FAD5B-D9CB-469F-A165-70867728950E/category/movie/104", LibraryKeys.Category(Playlist, LibrarySection.Movies, "104"));
    }

    [Fact]
    public void Live_items_are_keyed_by_stream_id()
    {
        var item = new PlaybackItem("ch-1", ContentKind.Live, "News", null, "u", StreamId: "42");

        Assert.EndsWith("/live/42", LibraryKeys.For(Playlist, item), StringComparison.Ordinal);
    }

    [Fact]
    public void Category_ids_are_recovered_per_section()
    {
        string[] keys =
        [
            LibraryKeys.Category(Playlist, LibrarySection.Live, "1"),
            LibraryKeys.Category(Playlist, LibrarySection.Live, "2"),
            LibraryKeys.Category(Playlist, LibrarySection.Movies, "3"),
            LibraryKeys.Category(Guid.NewGuid(), LibrarySection.Live, "9"),
        ];

        Assert.Equal(["1", "2"], LibraryKeys.CategoryIds(keys, Playlist, LibrarySection.Live));
        Assert.Equal(["3"], LibraryKeys.CategoryIds(keys, Playlist, LibrarySection.Movies));
    }

    [Fact]
    public void The_players_star_favourites_the_channel_the_movie_or_the_episodes_series()
    {
        var channel = new PlaybackItem("ch-1", ContentKind.Live, "News", null, "u", StreamId: "42");
        var movie = new PlaybackItem("7", ContentKind.Movie, "Film", null, "u");
        var episode = new PlaybackItem("e9", ContentKind.Episode, "Show", "S1 E2", "u", ParentId: "s3");

        Assert.Equal(LibraryKeys.Content(Playlist, ContentKind.Live, "42"), LibraryKeys.FavoriteFor(Playlist, channel));
        Assert.Equal(LibraryKeys.Content(Playlist, ContentKind.Movie, "7"), LibraryKeys.FavoriteFor(Playlist, movie));
        Assert.Equal(LibraryKeys.Content(Playlist, ContentKind.Series, "s3"), LibraryKeys.FavoriteFor(Playlist, episode));
    }

    [Fact]
    public void An_episode_without_its_series_has_nothing_to_favourite()
    {
        var orphan = new PlaybackItem("e9", ContentKind.Episode, "Show", "S1 E2", "u");

        Assert.Null(LibraryKeys.FavoriteFor(Playlist, orphan));
        Assert.Throws<ArgumentNullException>(() => LibraryKeys.FavoriteFor(Playlist, null!));
    }
}
