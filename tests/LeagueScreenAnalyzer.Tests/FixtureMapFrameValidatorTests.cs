using LeagueScreenAnalyzer.Capture.Fixtures;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class FixtureMapFrameValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsMissingWhenMinimapIsNotVisible()
    {
        RegionFrame frame = new(
            RegionType.Minimap,
            1,
            TimeSpan.Zero,
            300,
            300,
            new FixtureFramePayload("01:00", true, false));

        MapValidationResult result = await new FixtureMapFrameValidator().ValidateAsync(frame);

        Assert.Equal(MapFrameStatus.Missing, result.Status);
        Assert.NotEmpty(result.Reasons);
    }
}
