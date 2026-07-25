using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class NormalizedRegionTests
{
    [Fact]
    public void Constructor_AcceptsRegionInsideFrame()
    {
        NormalizedRegion region = new(0.1, 0.2, 0.3, 0.4);

        Assert.Equal(0.1, region.X);
        Assert.Equal(0.4, region.Height);
    }

    [Theory]
    [InlineData(double.NaN, 0, 0.1, 0.1)]
    [InlineData(double.PositiveInfinity, 0, 0.1, 0.1)]
    [InlineData(-0.1, 0, 0.1, 0.1)]
    [InlineData(0, 0, 0, 0.1)]
    [InlineData(0, 0, 0.1, 0)]
    [InlineData(0.9, 0, 0.2, 0.1)]
    [InlineData(0, 0.9, 0.1, 0.2)]
    public void Constructor_RejectsInvalidRegion(double x, double y, double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedRegion(x, y, width, height));
    }
}
