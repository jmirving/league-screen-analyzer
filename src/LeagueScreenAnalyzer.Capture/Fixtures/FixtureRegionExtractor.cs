using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Fixtures;

public sealed class FixtureRegionExtractor : IRegionExtractor
{
    public ValueTask<ExtractedRegions> ExtractAsync(
        SourceFrame sourceFrame,
        CaptureLayout captureLayout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFrame);
        ArgumentNullException.ThrowIfNull(captureLayout);
        cancellationToken.ThrowIfCancellationRequested();

        if (sourceFrame.Payload is not FixtureFramePayload payload)
        {
            throw new InvalidOperationException(
                $"FixtureRegionExtractor requires a {nameof(FixtureFramePayload)} source payload.");
        }

        RegionFrame clock = CreateRegion(sourceFrame, captureLayout.ClockRegion, RegionType.Clock, payload);
        RegionFrame minimap = CreateRegion(sourceFrame, captureLayout.MinimapRegion, RegionType.Minimap, payload);
        return ValueTask.FromResult(new ExtractedRegions(clock, minimap));
    }

    private static RegionFrame CreateRegion(
        SourceFrame sourceFrame,
        NormalizedRegion region,
        RegionType regionType,
        FixtureFramePayload payload)
    {
        int width = Math.Max(1, (int)Math.Round(sourceFrame.Width * region.Width));
        int height = Math.Max(1, (int)Math.Round(sourceFrame.Height * region.Height));
        return new RegionFrame(
            regionType,
            sourceFrame.SequenceNumber,
            sourceFrame.SourceTimestamp,
            width,
            height,
            payload);
    }
}
