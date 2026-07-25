using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Fixtures;

public sealed record FixtureFramePayload(
    string? ClockText,
    bool ClockVisible,
    bool MapVisible) : IFramePayload;
