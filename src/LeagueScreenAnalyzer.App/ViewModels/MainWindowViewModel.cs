namespace LeagueScreenAnalyzer.App.ViewModels;

public sealed class MainWindowViewModel
{
    public string Title => "League Screen Analyzer";

    public string MilestoneDescription => "Deterministic fixture processing architecture";

    public string SourceStatus => "No source selected — live window capture is not implemented.";

    public string ClockRegionStatus => "Clock region configuration will follow selected-window preview.";

    public string MinimapRegionStatus => "Minimap region configuration will follow selected-window preview.";

    public string SessionStatus => "Not capturing";

    public string CliGuidance => "Fixture processing is available through the CLI: process-fixture.";
}
