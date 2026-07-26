using LeagueScreenAnalyzer.Cli;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockEvaluationServiceTests
{
    [Fact]
    public async Task Evaluator_ReportsSyntheticFixtureMetrics()
    {
        string repository = FindRepositoryRoot();
        using TemporaryDirectory output = new();
        ClockEvaluationReport report = await new ClockEvaluationService().EvaluateAsync(
            "league-replay-v1",
            Path.Combine(repository, "fixtures", "clocks", "synthetic-seven-segment", "manifest.json"),
            output.Path);

        Assert.Equal(4, report.TotalSamples);
        Assert.Equal(2, report.CorrectlyAccepted);
        Assert.Equal(2, report.CorrectlyRejected);
        Assert.Equal(0, report.FalseAccepts);
        Assert.Equal(0, report.FalseRejects);
        Assert.Contains("synthetic", report.Provenance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluator_DiscoversOnlyLabeledDiagnosticBundles()
    {
        using TemporaryDirectory diagnostics = new();
        using TemporaryDirectory output = new();
        ClockRecognitionProfile profile =
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id);
        ClockDiagnosticWriter writer = new();

        Assert.True(ClockSampleLabelParser.TryParse(
            " 3:40 ",
            out ClockSampleLabel? label,
            out _));
        await WriteBundleAsync(
            writer,
            diagnostics.Path,
            profile,
            "3:40",
            sequence: 1,
            label,
            isUnlabeledDiagnostic: false);
        await WriteBundleAsync(
            writer,
            diagnostics.Path,
            profile,
            "9:59",
            sequence: 2,
            explicitLabel: null,
            isUnlabeledDiagnostic: true);

        ClockEvaluationReport report =
            await new ClockEvaluationService().EvaluateDiagnosticBundlesAsync(
                profile.Id,
                diagnostics.Path,
                output.Path);

        Assert.Equal(1, report.TotalSamples);
        Assert.Equal("3:40", Assert.Single(report.Samples).Label);
        Assert.Equal(1, report.CorrectlyAccepted);
        Assert.Contains("Human-labeled", report.Provenance, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output.Path, "clock-evaluation.json")));
    }

    private static async Task WriteBundleAsync(
        ClockDiagnosticWriter writer,
        string root,
        ClockRecognitionProfile profile,
        string visibleValue,
        long sequence,
        ClockSampleLabel? explicitLabel,
        bool isUnlabeledDiagnostic)
    {
        ClockImage image = ClockTestImages.Render(visibleValue, sequence: sequence);
        ClockRecognitionResult recognition =
            await new ConstrainedClockImageRecognizer().RecognizeAsync(image, profile);
        ClockReading reading =
            new ClockTemporalValidator().Validate(recognition, profile, sequence, TimeSpan.Zero);
        writer.Write(
            root,
            new ClockRecognitionObservation(image, recognition, reading, 4),
            profile,
            explicitLabel,
            isUnlabeledDiagnostic);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LeagueScreenAnalyzer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
