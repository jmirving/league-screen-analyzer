using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Tests;

public sealed class RegionLifecycleIntegrationTests
{
    [Theory]
    [InlineData(CaptureStatus.Idle, false)]
    [InlineData(CaptureStatus.Selecting, false)]
    [InlineData(CaptureStatus.Capturing, true)]
    [InlineData(CaptureStatus.Stopped, false)]
    [InlineData(CaptureStatus.Error, false)]
    public void EditingAvailability_FollowsCaptureLifecycle(CaptureStatus status, bool expected) =>
        Assert.Equal(expected, new CaptureState(status, Width: 1920, Height: 1080).IsCapturing);

    [Fact]
    public void Stop_DoesNotMutateCurrentLayout()
    {
        RegionEditor editor = new();
        NormalizedRegion clock = new(0.4, 0.01, 0.2, 0.1);
        NormalizedRegion minimap = new(0.7, 0.7, 0.2, 0.2);
        editor.Load(clock, minimap);

        _ = new CaptureState(CaptureStatus.Stopped, Width: 1920, Height: 1080);

        Assert.Equal(clock, editor.GetRegion(RegionType.Clock));
        Assert.Equal(minimap, editor.GetRegion(RegionType.Minimap));
    }

    [Fact]
    public void SourceResize_PreservesNormalizedCoordinatesAndChangesPixels()
    {
        NormalizedRegion region = new(0.4, 0.1, 0.2, 0.1);

        (int Width, int Height) first = PixelSize(region, 1920, 1080);
        (int Width, int Height) second = PixelSize(region, 2560, 1440);

        Assert.Equal(new NormalizedRegion(0.4, 0.1, 0.2, 0.1), region);
        Assert.Equal((384, 108), first);
        Assert.Equal((512, 144), second);
    }

    [Fact]
    public void MaterialAspectChange_UsesStrictTwoPercentThreshold()
    {
        SourceAspectRatioCompatibility compatibility = new(0.02);
        double expected = 16d / 9;

        Assert.False(compatibility.IsMaterialMismatch(expected, expected * 1.0199));
        Assert.True(compatibility.IsMaterialMismatch(expected, expected * 1.021));
    }

    private static (int Width, int Height) PixelSize(
        NormalizedRegion region,
        int sourceWidth,
        int sourceHeight) =>
        ((int)Math.Ceiling(region.Width * sourceWidth),
            (int)Math.Ceiling(region.Height * sourceHeight));
}
