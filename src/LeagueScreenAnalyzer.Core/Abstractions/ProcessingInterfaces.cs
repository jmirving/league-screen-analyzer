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

public interface IClockImageRecognizer
{
    ValueTask<ClockRecognitionResult> RecognizeAsync(
        ClockImage image,
        ClockRecognitionProfile profile,
        CancellationToken cancellationToken = default);
}

public interface IClockTemporalValidator
{
    ClockReading Validate(
        ClockRecognitionResult recognition,
        ClockRecognitionProfile profile,
        long sourceFrameSequence,
        TimeSpan sourceTimestamp);

    ClockTemporalContext Context { get; }

    void Reset();
}

public interface IMapFrameValidator
{
    ValueTask<MapValidationResult> ValidateAsync(RegionFrame minimapFrame, CancellationToken cancellationToken = default);
}

public interface IMapImageValidator
{
    ValueTask<MapValidationResult> ValidateAsync(
        MapImage minimapImage,
        CancellationToken cancellationToken = default);
}

public interface IObservationPolicy
{
    TimelineObservation Create(
        SourceFrame sourceFrame,
        RegionFrame minimapFrame,
        ClockReading clockResult,
        MapValidationResult mapResult,
        SessionMode mode);
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
