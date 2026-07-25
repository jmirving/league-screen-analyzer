using LeagueScreenAnalyzer.Capture.Fixtures;
using LeagueScreenAnalyzer.Capture.Processing;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Storage;

namespace LeagueScreenAnalyzer.Cli;

public sealed class FixtureProcessingService
{
    private static readonly CaptureLayout FixtureLayout = new(
        "Fixture layout",
        new NormalizedRegion(0.43, 0.01, 0.14, 0.08),
        new NormalizedRegion(0.78, 0.70, 0.21, 0.29));

    public async Task<SessionProcessingResult> ProcessAsync(
        string sourcePath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        FixtureFrameSource frameSource = new(sourcePath);
        FixtureRegionExtractor regionExtractor = new();
        ObservationProcessor observationProcessor = new(
            new FixtureGameClockReader(),
            new FixtureMapFrameValidator());
        SessionProcessor sessionProcessor = new(regionExtractor, observationProcessor);
        SessionProcessingResult result =
            await sessionProcessor.ProcessAsync(frameSource, FixtureLayout, cancellationToken).ConfigureAwait(false);

        JsonSessionArtifactWriter writer = new(outputPath);
        await writer.WriteAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
