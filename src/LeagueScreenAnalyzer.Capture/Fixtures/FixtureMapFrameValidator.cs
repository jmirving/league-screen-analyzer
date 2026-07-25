using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Fixtures;

public sealed class FixtureMapFrameValidator : IMapFrameValidator
{
    public ValueTask<MapValidationResult> ValidateAsync(
        RegionFrame minimapFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(minimapFrame);
        cancellationToken.ThrowIfCancellationRequested();

        if (minimapFrame.RegionType != RegionType.Minimap)
        {
            throw new ArgumentException("The supplied region is not a minimap region.", nameof(minimapFrame));
        }

        if (minimapFrame.Payload is not FixtureFramePayload payload)
        {
            throw new InvalidOperationException(
                $"FixtureMapFrameValidator requires a {nameof(FixtureFramePayload)} payload.");
        }

        return payload.MapVisible
            ? ValueTask.FromResult(new MapValidationResult(MapFrameStatus.Valid, 1, []))
            : ValueTask.FromResult(new MapValidationResult(
                MapFrameStatus.Missing,
                0,
                ["Minimap region is not visible."]));
    }
}
