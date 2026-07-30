using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Tests;

public sealed class RegionHandleDesignTests
{
    [Fact]
    public void VisibleMarker_IsSmallerThanUsableHitTarget()
    {
        Assert.InRange(RegionHandleDesign.VisibleSize, 5, 7);
        Assert.InRange(RegionHandleDesign.HitTargetSize, 16, 20);
        Assert.True(RegionHandleDesign.VisibleSize < RegionHandleDesign.HitTargetSize);
    }

    [Fact]
    public void ClockHasEdgesAndCorners_WhileMinimapHasCornersOnly()
    {
        Assert.Equal(8, RegionHandleDesign.For(RegionType.Clock).Count);
        Assert.Equal(
            [
                ResizeHandle.TopLeft,
                ResizeHandle.TopRight,
                ResizeHandle.BottomRight,
                ResizeHandle.BottomLeft
            ],
            RegionHandleDesign.For(RegionType.Minimap));
    }

    [Fact]
    public void SmallClockCrop_IsNotMostlyObscuredByVisibleMarkers()
    {
        const double cropWidth = 50;
        const double cropHeight = 15;
        double visibleArea = 4 * RegionHandleDesign.VisibleSize * RegionHandleDesign.VisibleSize;

        Assert.True(visibleArea < cropWidth * cropHeight / 4);
    }
}
