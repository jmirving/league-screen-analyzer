using LeagueScreenAnalyzer.Capture.Fixtures;
using LeagueScreenAnalyzer.Capture.Processing;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ObservationProcessorTests
{
    [Fact]
    public async Task ProcessAsync_RequiresValidClockAndMap()
    {
        SourceFrame source = CreateSourceFrame(mapVisible: true);
        FixtureRegionExtractor extractor = new();
        ExtractedRegions regions = await extractor.ExtractAsync(source, TestLayout);
        ObservationProcessor processor = new(new FixtureGameClockReader(), new FixtureMapFrameValidator());

        TimelineObservation observation = await processor.ProcessAsync(source, regions);

        Assert.Equal(ObservationStatus.Valid, observation.Status);
        Assert.NotNull(observation.GameTime);
    }

    [Fact]
    public async Task ProcessAsync_MakesObservationUnavailableWhenMinimapMissing()
    {
        SourceFrame source = CreateSourceFrame(mapVisible: false);
        FixtureRegionExtractor extractor = new();
        ExtractedRegions regions = await extractor.ExtractAsync(source, TestLayout);
        ObservationProcessor processor = new(new FixtureGameClockReader(), new FixtureMapFrameValidator());

        TimelineObservation observation = await processor.ProcessAsync(source, regions);

        Assert.Equal(ObservationStatus.Unavailable, observation.Status);
        Assert.Equal(MapFrameStatus.Missing, observation.MapResult.Status);
        Assert.Null(observation.GameTime);
    }

    private static readonly CaptureLayout TestLayout = new(
        "test",
        new NormalizedRegion(0, 0, 0.2, 0.1),
        new NormalizedRegion(0.8, 0.7, 0.2, 0.3));

    private static SourceFrame CreateSourceFrame(bool mapVisible) =>
        new(
            1,
            TimeSpan.Zero,
            1920,
            1080,
            new FixtureFramePayload("01:00", true, mapVisible));
}
