using Retune.Core.Models;

namespace Retune.Core.Tests;

public sealed class WindowPlacementTests
{
    private static readonly (double, double, double, double)[] OneScreen = [(0, 0, 1920, 1080)];
    private static readonly (double, double, double, double)[] TwoScreens = [(0, 0, 1920, 1080), (1920, 0, 2560, 1440)];

    [Fact]
    public void A_window_on_a_screen_is_restored()
    {
        Assert.True(new WindowPlacement(100, 100, 1200, 800, false).IsOnScreen(OneScreen));
        Assert.True(new WindowPlacement(2200, 200, 1200, 800, true).IsOnScreen(TwoScreens));
    }

    [Theory]
    [InlineData(2200, 200)]   // was on a monitor that's gone
    [InlineData(-5000, 100)]
    [InlineData(100, -500)]   // title bar above the screen
    [InlineData(1880, 100)]   // only a sliver left on screen
    public void A_window_off_every_screen_is_not_restored(double left, double top) =>
        Assert.False(new WindowPlacement(left, top, 1200, 800, false).IsOnScreen(OneScreen));

    [Theory]
    [InlineData(double.NaN, 0, 1200, 800)]
    [InlineData(0, 0, 200, 800)]
    [InlineData(0, 0, 1200, double.PositiveInfinity)]
    [InlineData(0, 0, 1e9, 800)]
    public void Broken_sizes_are_invalid(double left, double top, double width, double height) =>
        Assert.False(new WindowPlacement(left, top, width, height, false).IsValid(980, 620));

    [Fact]
    public void A_normal_size_is_valid() => Assert.True(new WindowPlacement(10, 10, 1360, 860, false).IsValid(980, 620));
}