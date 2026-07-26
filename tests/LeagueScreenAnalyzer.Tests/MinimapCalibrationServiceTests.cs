using LeagueScreenAnalyzer.Cli;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class MinimapCalibrationServiceTests
{
    [Fact]
    public async Task AnalyzeBuildAndEvaluate_UseExplicitLabelsAndExcludeUncertain()
    {
        string fixtures = Path.Combine(
            FindRepositoryRoot(),
            "fixtures",
            "minimaps",
            "diagnostics");
        using TemporaryDirectory output = new();
        MinimapCalibrationService service = new();

        MinimapAnalysisReport analysis = await service.AnalyzeAsync(
            fixtures,
            Path.Combine(output.Path, "analysis"));
        var profile = await service.BuildProfileAsync(
            "test-built-map-v1",
            fixtures,
            Path.Combine(output.Path, "profile"));
        MinimapEvaluationReport evaluation = await service.EvaluateAsync(
            BuiltInMinimapProfiles.LeagueReplayMinimapV1Id,
            fixtures,
            Path.Combine(output.Path, "evaluation"));

        Assert.Equal(4, analysis.TotalSamples);
        Assert.Equal(1, analysis.ValidSamples);
        Assert.Equal(2, analysis.InvalidSamples);
        Assert.Equal(1, evaluation.UncertainSamples);
        Assert.Equal(3, evaluation.TotalLabeledSamples);
        Assert.Equal(0, evaluation.FalsePositives);
        Assert.True(profile.CalibratedForCanonicalRecording);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LeagueScreenAnalyzer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
