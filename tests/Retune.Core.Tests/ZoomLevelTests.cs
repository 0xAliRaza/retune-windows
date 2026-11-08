using Retune.Core.Models;

namespace Retune.Core.Tests;

public sealed class ZoomLevelTests
{
    [Theory]
    [InlineData(1, 1.1)]
    [InlineData(1.1, 1.25)]
    [InlineData(1.17, 1.25)]
    [InlineData(1.75, 2)]
    [InlineData(2, 2)]
    [InlineData(0.5, 0.8)]
    public void In_goes_to_the_next_step(double current, double expected) => Assert.Equal(expected, ZoomLevel.In(current));

    [Theory]
    [InlineData(1, 0.9)]
    [InlineData(1.17, 1.1)]
    [InlineData(0.8, 0.75)]
    [InlineData(0.75, 0.75)]
    [InlineData(5, 1.75)]
    public void Out_goes_to_the_previous_step(double current, double expected) => Assert.Equal(expected, ZoomLevel.Out(current));

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(0.1, 0.75)]
    [InlineData(9, 2)]
    [InlineData(1.3, 1.3)]
    public void Clamped_keeps_values_in_range_and_repairs_garbage(double value, double expected) => Assert.Equal(expected, ZoomLevel.Clamped(value));

    [Theory]
    [InlineData(1, "100%")]
    [InlineData(1.25, "125%")]
    [InlineData(0.75, "75%")]
    public void Label(double value, string expected) => Assert.Equal(expected, ZoomLevel.Label(value));

    [Fact]
    public void Steps_are_ascending_and_include_100_percent()
    {
        Assert.Equal(ZoomLevel.Steps.Order(), ZoomLevel.Steps);
        Assert.Contains(ZoomLevel.Default, ZoomLevel.Steps);
    }
}
