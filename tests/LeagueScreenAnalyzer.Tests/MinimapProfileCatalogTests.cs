using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class MinimapProfileCatalogTests
{
    [Fact]
    public void DefaultCatalog_DiscoversPackagedLeagueReplayProfile()
    {
        MinimapProfileCatalog catalog = MinimapProfileCatalog.CreateDefault();

        MinimapProfileCatalogEntry entry =
            catalog.Get(BuiltInMinimapProfiles.LeagueReplayMinimapV1Id);
        Assert.Contains(entry, catalog.Profiles);
        Assert.Equal(1, entry.Version);
        Assert.Equal("Calibration-oriented", entry.CalibrationStatus);
        Assert.NotNull(entry.SourcePath);
        Assert.EndsWith(
            Path.Combine(
                "fixtures",
                "minimaps",
                "league-replay-minimap-v1",
                "profile.json"),
            entry.SourcePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            BuiltInMinimapProfiles.LeagueReplayMinimapV1,
            MinimapProfileSerializer.Load(entry.SourcePath!));
    }

    [Fact]
    public void Catalog_RejectsMalformedManifestClearly()
    {
        using TemporaryDirectory temporary = new();
        string profileDirectory = Path.Combine(temporary.Path, "broken");
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(Path.Combine(profileDirectory, "profile.json"), "{ broken");

        MinimapProfileCatalog catalog = MinimapProfileCatalog.Discover(
            [new MinimapProfileSearchRoot(
                temporary.Path,
                MinimapProfileProvenance.DevelopmentOverride)]);

        MinimapProfileCatalogError error = Assert.Single(catalog.Errors);
        Assert.Contains("Rejected minimap profile", error.Message);
        Assert.Contains("profile.json", error.ProfilePath);
    }

    [Fact]
    public async Task ConflictingStableId_IsRejectedAndErrorIsNotSilent()
    {
        using TemporaryDirectory temporary = new();
        string profileDirectory = Path.Combine(
            temporary.Path,
            BuiltInMinimapProfiles.LeagueReplayMinimapV1Id);
        Directory.CreateDirectory(profileDirectory);
        await MinimapProfileSerializer.SaveAsync(
            BuiltInMinimapProfiles.LeagueReplayMinimapV1 with
            {
                DisplayName = "Conflicting replacement"
            },
            Path.Combine(profileDirectory, "profile.json"));

        MinimapProfileCatalog catalog = MinimapProfileCatalog.Discover(
            [new MinimapProfileSearchRoot(
                temporary.Path,
                MinimapProfileProvenance.UserInstalled)]);

        MinimapProfileCatalogEntry entry =
            catalog.Get(BuiltInMinimapProfiles.LeagueReplayMinimapV1Id);
        Assert.Equal(
            BuiltInMinimapProfiles.LeagueReplayMinimapV1,
            entry.Profile);
        Assert.NotEqual("Conflicting replacement", entry.DisplayName);
        Assert.Contains(
            catalog.Errors,
            error => error.Message.Contains(
                "conflicts with the built-in",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_MissingProfileProducesActionableError()
    {
        MinimapProfileCatalog catalog = MinimapProfileCatalog.CreateDefault();

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
            () => catalog.Resolve("missing-minimap-profile"));

        Assert.Contains("neither an available stable ID", exception.Message);
    }

    [Fact]
    public async Task RuntimeValidationResult_ReportsExactCatalogProfileId()
    {
        MinimapValidationProfile profile = MinimapProfileCatalog.CreateDefault()
            .Get(BuiltInMinimapProfiles.LeagueReplayMinimapV1Id)
            .Profile;
        byte[] pixels = new byte[128 * 128 * 4];
        using MapImage image = new(128, 128, 128 * 4, pixels, 7, TimeSpan.Zero);

        MapValidationResult result =
            await new StructuralMinimapValidator(profile).ValidateAsync(image);

        Assert.Equal("league-replay-minimap-v1", result.ProfileId);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"league-screen-analyzer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
