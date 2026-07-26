namespace LeagueScreenAnalyzer.Core.Models;

public sealed record CaptureLayout
{
    public CaptureLayout(
        string name,
        NormalizedRegion clockRegion,
        NormalizedRegion minimapRegion,
        double? sourceAspectRatio = null,
        string? clockProfileId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sourceAspectRatio is not null
            && (!double.IsFinite(sourceAspectRatio.Value) || sourceAspectRatio <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAspectRatio));
        }

        Name = name;
        ClockRegion = clockRegion ?? throw new ArgumentNullException(nameof(clockRegion));
        MinimapRegion = minimapRegion ?? throw new ArgumentNullException(nameof(minimapRegion));
        SourceAspectRatio = sourceAspectRatio;
        ClockProfileId = string.IsNullOrWhiteSpace(clockProfileId) ? null : clockProfileId;
    }

    public string Name { get; }

    public NormalizedRegion ClockRegion { get; }

    public NormalizedRegion MinimapRegion { get; }

    public double? SourceAspectRatio { get; }

    public string? ClockProfileId { get; }
}
