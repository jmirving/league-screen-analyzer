namespace LeagueScreenAnalyzer.Core.Models;

public sealed record CaptureLayout
{
    public CaptureLayout(string name, NormalizedRegion clockRegion, NormalizedRegion minimapRegion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        ClockRegion = clockRegion ?? throw new ArgumentNullException(nameof(clockRegion));
        MinimapRegion = minimapRegion ?? throw new ArgumentNullException(nameof(minimapRegion));
    }

    public string Name { get; }

    public NormalizedRegion ClockRegion { get; }

    public NormalizedRegion MinimapRegion { get; }
}
