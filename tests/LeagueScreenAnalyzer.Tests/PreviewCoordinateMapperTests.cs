using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Tests;

public sealed class PreviewCoordinateMapperTests
{
    private readonly PreviewCoordinateMapper _mapper = new();

    [Fact]
    public void MatchingAspectRatio_UsesEntirePreview()
    {
        PreviewViewport viewport = _mapper.CalculateViewport(
            new CoordinateSize(1920, 1080),
            new CoordinateSize(960, 540));

        Assert.Equal(new PreviewViewport(0, 0, 960, 540), viewport);
    }

    [Fact]
    public void Letterboxing_CentersVideoVertically()
    {
        PreviewViewport viewport = _mapper.CalculateViewport(
            new CoordinateSize(1920, 1080),
            new CoordinateSize(1000, 1000));

        Assert.Equal(0, viewport.X, 8);
        Assert.Equal(218.75, viewport.Y, 8);
        Assert.Equal(1000, viewport.Width, 8);
        Assert.Equal(562.5, viewport.Height, 8);
    }

    [Fact]
    public void Pillarboxing_CentersVideoHorizontally()
    {
        PreviewViewport viewport = _mapper.CalculateViewport(
            new CoordinateSize(1000, 1000),
            new CoordinateSize(1600, 900));

        Assert.Equal(350, viewport.X, 8);
        Assert.Equal(0, viewport.Y, 8);
        Assert.Equal(900, viewport.Width, 8);
        Assert.Equal(900, viewport.Height, 8);
    }

    [Fact]
    public void PreviewToNormalized_AccountsForUnusedSpace()
    {
        NormalizedPoint? point = _mapper.PreviewToNormalized(
            new CoordinatePoint(500, 500),
            new CoordinateSize(1920, 1080),
            new CoordinateSize(1000, 1000));

        Assert.NotNull(point);
        Assert.Equal(0.5, point.Value.X, 8);
        Assert.Equal(0.5, point.Value.Y, 8);
    }

    [Fact]
    public void NormalizedToPreview_MapsThroughViewport()
    {
        CoordinatePoint point = _mapper.NormalizedToPreview(
            new NormalizedPoint(0.25, 0.75),
            new CoordinateSize(1000, 1000),
            new CoordinateSize(1600, 900));

        Assert.Equal(575, point.X, 8);
        Assert.Equal(675, point.Y, 8);
    }

    [Fact]
    public void OutsideRenderedVideo_ReturnsNull()
    {
        NormalizedPoint? point = _mapper.PreviewToNormalized(
            new CoordinatePoint(500, 100),
            new CoordinateSize(1920, 1080),
            new CoordinateSize(1000, 1000));

        Assert.Null(point);
    }

    [Fact]
    public void Conversion_RoundTripsWithinTolerance()
    {
        NormalizedPoint original = new(0.123456, 0.876543);
        CoordinatePoint preview = _mapper.NormalizedToPreview(
            original,
            new CoordinateSize(2560, 1380),
            new CoordinateSize(777, 333));
        NormalizedPoint? roundTrip = _mapper.PreviewToNormalized(
            preview,
            new CoordinateSize(2560, 1380),
            new CoordinateSize(777, 333));

        Assert.NotNull(roundTrip);
        Assert.Equal(original.X, roundTrip.Value.X, 10);
        Assert.Equal(original.Y, roundTrip.Value.Y, 10);
    }

    [Fact]
    public void RegionToPreview_MapsAllFourValues()
    {
        CoordinateRect rect = _mapper.NormalizedRegionToPreview(
            new NormalizedRegion(0.1, 0.2, 0.3, 0.4),
            new CoordinateSize(1000, 1000),
            new CoordinateSize(1600, 900));

        Assert.Equal(new CoordinateRect(440, 180, 270, 360), rect);
    }
}
