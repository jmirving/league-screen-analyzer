using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Processing;

public sealed class ObservationPolicy : IObservationPolicy
{
    public TimelineObservation Create(
        SourceFrame sourceFrame,
        RegionFrame minimapFrame,
        ClockReading clockResult,
        MapValidationResult mapResult,
        SessionMode mode)
    {
        ArgumentNullException.ThrowIfNull(sourceFrame);
        ArgumentNullException.ThrowIfNull(minimapFrame);
        ArgumentNullException.ThrowIfNull(clockResult);
        ArgumentNullException.ThrowIfNull(mapResult);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        bool sameFrame =
            minimapFrame.SourceFrameSequence == sourceFrame.SequenceNumber &&
            minimapFrame.SourceTimestamp == sourceFrame.SourceTimestamp &&
            clockResult.SourceFrameSequence == sourceFrame.SequenceNumber &&
            clockResult.SourceTimestamp == sourceFrame.SourceTimestamp &&
            mapResult.SourceFrameSequence == sourceFrame.SequenceNumber &&
            mapResult.SourceTimestamp == sourceFrame.SourceTimestamp;
        bool accepted =
            sameFrame &&
            clockResult.Status == ClockReadingStatus.Valid &&
            clockResult.TemporalStatus == ClockTemporalStatus.Accepted &&
            mapResult.Status == MapFrameStatus.Valid;

        string? reason = accepted
            ? null
            : !sameFrame
                ? "source-frame-mismatch"
                : clockResult.Status != ClockReadingStatus.Valid ||
                  clockResult.TemporalStatus != ClockTemporalStatus.Accepted
                    ? "clock-unavailable"
                    : "minimap-unavailable";

        return new TimelineObservation(
            sourceFrame.SourceTimestamp,
            accepted ? clockResult.GameTime : null,
            accepted ? ObservationStatus.Valid : ObservationStatus.Unavailable,
            clockResult,
            mapResult,
            sourceFrameSequence: sourceFrame.SequenceNumber,
            unavailabilityReason: reason);
    }
}
