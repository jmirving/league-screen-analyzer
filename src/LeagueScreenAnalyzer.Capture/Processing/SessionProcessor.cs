using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Processing;

public sealed class SessionProcessor(
    IRegionExtractor regionExtractor,
    IObservationProcessor observationProcessor)
{
    private readonly IRegionExtractor _regionExtractor =
        regionExtractor ?? throw new ArgumentNullException(nameof(regionExtractor));
    private readonly IObservationProcessor _observationProcessor =
        observationProcessor ?? throw new ArgumentNullException(nameof(observationProcessor));

    public async Task<SessionProcessingResult> ProcessAsync(
        IFrameSource frameSource,
        CaptureLayout captureLayout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameSource);
        ArgumentNullException.ThrowIfNull(captureLayout);

        List<TimelineObservation> observations = [];

        await foreach (SourceFrame frame in frameSource.ReadFramesAsync(cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            ExtractedRegions regions =
                await _regionExtractor.ExtractAsync(frame, captureLayout, cancellationToken).ConfigureAwait(false);
            TimelineObservation observation =
                await _observationProcessor.ProcessAsync(frame, regions, cancellationToken).ConfigureAwait(false);
            observations.Add(observation);
        }

        IReadOnlyList<GapInterval> gaps = GapDetector.Detect(observations);
        SessionSummary summary = CreateSummary(observations, gaps);
        return new SessionProcessingResult(observations, gaps, summary);
    }

    private static SessionSummary CreateSummary(
        IReadOnlyList<TimelineObservation> observations,
        IReadOnlyList<GapInterval> gaps)
    {
        TimelineObservation[] valid = observations
            .Where(observation => observation.Status == ObservationStatus.Valid)
            .ToArray();

        return new SessionSummary(
            observations.Count,
            valid.Length,
            observations.Count - valid.Length,
            valid.FirstOrDefault()?.GameTime,
            valid.LastOrDefault()?.GameTime,
            gaps.Count,
            observations.Count(observation =>
                observation.ClockResult.Status is ClockReadingStatus.Implausible
                    or ClockReadingStatus.Backward
                    or ClockReadingStatus.Discontinuous
                    or ClockReadingStatus.RejectedJump));
    }
}
