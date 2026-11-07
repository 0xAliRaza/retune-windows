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
}
