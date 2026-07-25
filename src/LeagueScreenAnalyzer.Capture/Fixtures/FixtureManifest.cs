using System.Text.Json.Serialization;

namespace LeagueScreenAnalyzer.Capture.Fixtures;

public sealed class FixtureManifest
{
    [JsonPropertyName("frames")]
    public IReadOnlyList<FixtureFrameDefinition> Frames { get; init; } = [];
}

public sealed class FixtureFrameDefinition
{
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("sourceTimeMs")]
    public long SourceTimeMs { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("clockText")]
    public string? ClockText { get; init; }

    [JsonPropertyName("clockVisible")]
    public bool ClockVisible { get; init; }

    [JsonPropertyName("mapVisible")]
    public bool MapVisible { get; init; }
}
