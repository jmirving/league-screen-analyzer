using System.Runtime.CompilerServices;
using LeagueScreenAnalyzer.Capture.Fixtures;
using LeagueScreenAnalyzer.Capture.Processing;
using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class SessionProcessorTests
{
    [Fact]
    public async Task ProcessAsync_GeneratesGapBetweenValidAnchors()
    {
        SourceFrame[] frames =
        [
            CreateFrame(1, 0, "10:00", true, true),
            CreateFrame(2, 1000, null, false, false),
            CreateFrame(3, 2000, "10:02", true, true)
        ];
        SessionProcessor processor = new(
            new FixtureRegionExtractor(),
            new ObservationProcessor(new FixtureGameClockReader(), new FixtureMapFrameValidator()));

        SessionProcessingResult result = await processor.ProcessAsync(
            new InMemoryFrameSource(frames),
            TestLayout);

        GapInterval gap = Assert.Single(result.Gaps);
        Assert.Equal(TimeSpan.FromMinutes(10), gap.StartGameTime);
        Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2), gap.EndGameTime);
        Assert.Equal(1, result.Summary.DetectedGapCount);
    }

    private static readonly CaptureLayout TestLayout = new(
        "test",
        new NormalizedRegion(0, 0, 0.2, 0.1),
        new NormalizedRegion(0.8, 0.7, 0.2, 0.3));

    private static SourceFrame CreateFrame(
        long sequence,
        long sourceTimeMs,
        string? clockText,
        bool clockVisible,
        bool mapVisible) =>
        new(
            sequence,
            TimeSpan.FromMilliseconds(sourceTimeMs),
            1920,
            1080,
            new FixtureFramePayload(clockText, clockVisible, mapVisible));

    private sealed class InMemoryFrameSource(IReadOnlyList<SourceFrame> frames) : IFrameSource
    {
        public async IAsyncEnumerable<SourceFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (SourceFrame frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frame;
                await Task.Yield();
            }
        }
    }
}
