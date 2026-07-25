using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Core.Abstractions;

public interface IFrameSource
{
    IAsyncEnumerable<SourceFrame> ReadFramesAsync(CancellationToken cancellationToken = default);
}

public interface IRegionExtractor
{
    ValueTask<ExtractedRegions> ExtractAsync(
        SourceFrame sourceFrame,
        CaptureLayout captureLayout,
        CancellationToken cancellationToken = default);
}

public interface IGameClockReader
{
    ValueTask<ClockReading> ReadAsync(RegionFrame clockFrame, CancellationToken cancellationToken = default);
}

public interface IMapFrameValidator
{
    ValueTask<MapValidationResult> ValidateAsync(RegionFrame minimapFrame, CancellationToken cancellationToken = default);
}

public interface IObservationProcessor
{
    ValueTask<TimelineObservation> ProcessAsync(
        SourceFrame sourceFrame,
        ExtractedRegions regions,
        CancellationToken cancellationToken = default);
}

public interface ISessionArtifactWriter
{
    Task WriteAsync(SessionProcessingResult result, CancellationToken cancellationToken = default);
}
