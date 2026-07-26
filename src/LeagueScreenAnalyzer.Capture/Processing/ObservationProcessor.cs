using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Processing;

public sealed class ObservationProcessor(
    IGameClockReader clockReader,
    IMapFrameValidator mapValidator,
    IObservationPolicy? observationPolicy = null,
    SessionMode sessionMode = SessionMode.ReplayContinuous) : IObservationProcessor
{
    private readonly IGameClockReader _clockReader =
        clockReader ?? throw new ArgumentNullException(nameof(clockReader));
    private readonly IMapFrameValidator _mapValidator =
        mapValidator ?? throw new ArgumentNullException(nameof(mapValidator));
    private readonly IObservationPolicy _observationPolicy =
        observationPolicy ?? new ObservationPolicy();
    private readonly SessionMode _sessionMode = sessionMode;

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
        return _observationPolicy.Create(
            sourceFrame,
            regions.Minimap,
            clockResult,
            mapResult,
            _sessionMode);
    }
}
