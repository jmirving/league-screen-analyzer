using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Processing;

public sealed class ObservationProcessor(
    IGameClockReader clockReader,
    IMapFrameValidator mapValidator) : IObservationProcessor
{
    private readonly IGameClockReader _clockReader =
        clockReader ?? throw new ArgumentNullException(nameof(clockReader));
    private readonly IMapFrameValidator _mapValidator =
        mapValidator ?? throw new ArgumentNullException(nameof(mapValidator));

    public async ValueTask<TimelineObservation> ProcessAsync(
        SourceFrame sourceFrame,
        ExtractedRegions regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFrame);
        ArgumentNullException.ThrowIfNull(regions);

        ClockReading clockResult =
            await _clockReader.ReadAsync(regions.Clock, cancellationToken).ConfigureAwait(false);
        MapValidationResult mapResult =
            await _mapValidator.ValidateAsync(regions.Minimap, cancellationToken).ConfigureAwait(false);
        bool isValid =
            clockResult.Status == ClockReadingStatus.Valid &&
            mapResult.Status == MapFrameStatus.Valid;

        return new TimelineObservation(
            sourceFrame.SourceTimestamp,
            isValid ? clockResult.GameTime : null,
            isValid ? ObservationStatus.Valid : ObservationStatus.Unavailable,
            clockResult,
            mapResult);
    }
}
