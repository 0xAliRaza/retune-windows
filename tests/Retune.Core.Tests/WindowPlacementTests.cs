using Retune.Core.Models;

namespace Retune.Core.Tests;

public sealed class WindowPlacementTests
{
    [Theory]
    [InlineData(double.NaN, 0, 1200, 800)]
    [InlineData(0, 0, 200, 800)]
    [InlineData(0, 0, 1200, double.PositiveInfinity)]
    [InlineData(0, 0, 1e9, 800)]
    public void Broken_sizes_are_invalid(double left, double top, double width, double height) =>
        Assert.False(new WindowPlacement(left, top, width, height, false).IsValid(980, 620));

    [Fact]
    public void A_normal_size_is_valid_even_on_a_monitor_left_of_the_primary() =>
        Assert.True(new WindowPlacement(-1700, 10, 1360, 860, false).IsValid(980, 620));
}
