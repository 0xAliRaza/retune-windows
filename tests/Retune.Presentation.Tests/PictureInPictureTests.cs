using Retune.Presentation.Player;

namespace Retune.Presentation.Tests;

public sealed class PictureInPictureTests
{
    private static readonly PixelBounds LaptopWorkArea = new(3840, 546, 1920, 1152);

    [Fact]
    public void Switching_away_opens_the_mini_player_only_while_something_plays()
    {
        Assert.True(PictureInPicture.OpensOnSwitchAway(enabled: true, presented: true, paused: false, minimized: false, alreadyOpen: false));

        Assert.False(PictureInPicture.OpensOnSwitchAway(enabled: false, presented: true, paused: false, minimized: false, alreadyOpen: false));
        Assert.False(PictureInPicture.OpensOnSwitchAway(enabled: true, presented: false, paused: false, minimized: false, alreadyOpen: false));
        Assert.False(PictureInPicture.OpensOnSwitchAway(enabled: true, presented: true, paused: true, minimized: false, alreadyOpen: false));
        Assert.False(PictureInPicture.OpensOnSwitchAway(enabled: true, presented: true, paused: false, minimized: true, alreadyOpen: false));
        Assert.False(PictureInPicture.OpensOnSwitchAway(enabled: true, presented: true, paused: false, minimized: false, alreadyOpen: true));
    }

    [Fact]
    public void The_mini_player_sits_in_the_bottom_right_corner_scaled_for_the_screen()
    {
        var placed = PictureInPicture.Place(LaptopWorkArea, dpiScale: 1.25);

        Assert.Equal(new PixelBounds(3840 + 1920 - 20 - 600, 546 + 1152 - 20 - 338, 600, 338), placed);
    }

    [Fact]
    public void A_small_screen_keeps_the_mini_player_to_half_its_width_in_sixteen_by_nine()
    {
        var placed = PictureInPicture.Place(new PixelBounds(0, 0, 800, 600), 1);

        Assert.Equal(400, placed.Width);
        Assert.Equal(225, placed.Height);
        Assert.Equal(800 - 16, placed.Right);
        Assert.Equal(600 - 16, placed.Bottom);
    }

    [Fact]
    public void A_very_short_screen_keeps_the_mini_player_inside_it()
    {
        var placed = PictureInPicture.Place(new PixelBounds(0, 0, 3000, 200), 1);

        Assert.True(placed.Y >= 0);
        Assert.Equal(168, placed.Height);
        Assert.Equal(299, placed.Width);
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(100, 0, 1)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 100, double.NaN)]
    public void Nonsense_screens_are_rejected(int width, int height, double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PictureInPicture.Place(new PixelBounds(0, 0, width, height), scale));
}
