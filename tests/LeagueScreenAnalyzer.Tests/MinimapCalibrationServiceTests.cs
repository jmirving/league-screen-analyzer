using LeagueScreenAnalyzer.Cli;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class MinimapCalibrationServiceTests
{
    [Theory]
    [InlineData("league-replay-minimap-v1", 1)]
    [InlineData("league-replay-minimap-v2", 2)]
    [InlineData("league-replay-minimap-v10", 10)]
    public async Task BuildProfile_DerivesVersionAndFamilyFromStableId(
        string profileId,
        int expectedVersion)
    {
        using TemporaryDirectory output = new();

        var profile = await new MinimapCalibrationService().BuildProfileAsync(
            profileId,
            DiagnosticsDirectory(),
            output.Path);

        Assert.Equal(expectedVersion, profile.Version);
        Assert.Equal("league-replay-minimap", profile.FamilyId);
        Assert.Equal(profile, MinimapProfileSerializer.Load(
            Path.Combine(output.Path, "profile.json")));
    }

    [Fact]
    public async Task BuildProfile_RejectsExplicitVersionMismatchBeforeWriting()
    {
        using TemporaryDirectory output = new();
        string profileDirectory = Path.Combine(output.Path, "profile");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new MinimapCalibrationService().BuildProfileAsync(
                "league-replay-minimap-v2",
                DiagnosticsDirectory(),
                profileDirectory,
                profileVersion: 1));

        Assert.Contains("stable ID declares v2", exception.Message);
        Assert.False(Directory.Exists(profileDirectory));
        Assert.False(File.Exists(Path.Combine(profileDirectory, "profile.json")));
    }

    [Theory]
    [InlineData("league-replay-minimap")]
    [InlineData("league-replay-minimap-v")]
    [InlineData("league-replay-minimap-v0")]
    [InlineData("league-replay-minimap-v-1")]
    [InlineData("league-replay-minimap-v01")]
    public async Task BuildProfile_RejectsMalformedStableIdBeforeWriting(string profileId)
    {
        using TemporaryDirectory output = new();
        string profileDirectory = Path.Combine(output.Path, "profile");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new MinimapCalibrationService().BuildProfileAsync(
                profileId,
                DiagnosticsDirectory(),
                profileDirectory));

        Assert.False(Directory.Exists(profileDirectory));
        Assert.False(File.Exists(Path.Combine(profileDirectory, "profile.json")));
    }

    [Fact]
    public async Task GeneratedV2_LoadsThroughSharedCliAndDesktopCatalog()
    {
        using TemporaryDirectory output = new();
        await new MinimapCalibrationService().BuildProfileAsync(
            "league-replay-minimap-v2",
            DiagnosticsDirectory(),
            Path.Combine(output.Path, "league-replay-minimap-v2"));

        MinimapProfileCatalog catalog = MinimapProfileCatalog.Discover(
            [new MinimapProfileSearchRoot(
                output.Path,
                MinimapProfileProvenance.DevelopmentOverride)]);

        MinimapProfileCatalogEntry entry = catalog.Get("league-replay-minimap-v2");
        Assert.Equal(2, entry.Version);
        Assert.Equal("league-replay-minimap", entry.Family);
        Assert.Empty(catalog.Errors);
        Assert.Equal("league-replay-minimap-v2", catalog.DefaultProfile.Id);
    }

    [Fact]
    public async Task AnalyzeBuildAndEvaluate_UseExplicitLabelsAndExcludeUncertain()
    {
        string fixtures = DiagnosticsDirectory();
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

    private static string DiagnosticsDirectory() => Path.Combine(
        FindRepositoryRoot(),
        "fixtures",
        "minimaps",
        "diagnostics");

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
