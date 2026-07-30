using System.Text.Json;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;
using LeagueScreenAnalyzer.Storage;

namespace LeagueScreenAnalyzer.Tests;

[Collection("Clock profile discovery")]
public sealed class ClockProfileCatalogTests
{
    [Fact]
    public void DefaultCatalog_DiscoversPackagedV1V2AndGeneratedV3()
    {
        ClockProfileCatalog catalog = ClockProfileCatalog.CreateDefault();

        Assert.Empty(catalog.Errors);
        Assert.Equal(
            ["league-replay-v1", "league-replay-v2", "league-replay-v3"],
            catalog.Profiles.Select(profile => profile.Id).ToArray());
        Assert.Equal(ClockProfileProvenance.BuiltIn, catalog.Get("league-replay-v1").Provenance);
        Assert.Equal(65, catalog.Get("league-replay-v2").TemplateCount);
        Assert.Equal(135, catalog.Get("league-replay-v3").TemplateCount);
        Assert.Equal(3, catalog.Get("league-replay-v3").Version);
        Assert.NotNull(catalog.Get("league-replay-v3").SourceManifestPath);
    }

    [Fact]
    public void GeneratedV3_LoadsAll135Templates()
    {
        ClockProfileCatalogEntry v3 = ClockProfileCatalog.CreateDefault().Get("league-replay-v3");
        string directory = Path.GetDirectoryName(v3.SourceManifestPath!)!;

        Assert.Equal(135, ClockTemplateProfileLoader.LoadTemplates(directory).Count);
    }

    [Fact]
    public void CliCompatibilityLookup_AndCatalogResolveSameProfile()
    {
        ClockProfileCatalog catalog = ClockProfileCatalog.CreateDefault();

        ClockRecognitionProfile cliProfile = BuiltInClockProfiles.Get("league-replay-v3");
        ClockProfileCatalogEntry shared = catalog.Get("league-replay-v3");

        Assert.Equal(shared.Profile, cliProfile);
        Assert.Equal(
            Path.GetDirectoryName(shared.SourceManifestPath),
            ClockTemplateProfileLoader.FindProfileDirectory("league-replay-v3"));
    }

    [Fact]
    public void DuplicateGeneratedIds_AreRejectedWithoutReplacement()
    {
        using TemporaryDirectory root = new();
        WriteProfile(root.Path, "first", "duplicate-v1", includeImage: true);
        WriteProfile(root.Path, "second", "duplicate-v1", includeImage: true);

        ClockProfileCatalog catalog = Discover(root.Path);

        Assert.False(catalog.TryGet("duplicate-v1", out _));
        Assert.Contains(catalog.Errors, error => error.Message.Contains(
            "Duplicate clock profile ID 'duplicate-v1'", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedManifest_IsRejectedClearly()
    {
        using TemporaryDirectory root = new();
        string directory = Path.Combine(root.Path, "malformed");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            """{"schemaVersion":1,"profileId":"malformed"}""");

        ClockProfileCatalog catalog = Discover(root.Path);

        ClockProfileCatalogError error = Assert.Single(catalog.Errors);
        Assert.Contains("Rejected clock profile manifest", error.Message);
        Assert.Contains("header is malformed", error.Message);
        Assert.False(catalog.TryGet("malformed", out _));
    }

    [Fact]
    public void MissingTemplateAsset_IsRejectedClearly()
    {
        using TemporaryDirectory root = new();
        WriteProfile(root.Path, "missing", "league-replay-v4", includeImage: false, version: 4);

        ClockProfileCatalog catalog = Discover(root.Path);

        Assert.Contains(catalog.Errors, error =>
            error.Message.Contains("Template image is missing", StringComparison.Ordinal));
        Assert.False(catalog.TryGet("league-replay-v4", out _));
    }

    [Fact]
    public void Profiles_AreSortedByStableId_AndLabelsAreDistinctAndExposeIds()
    {
        using TemporaryDirectory root = new();
        WriteProfile(root.Path, "z", "league-replay-v5", includeImage: true, version: 5);
        WriteProfile(root.Path, "a", "league-replay-v4", includeImage: true, version: 4);

        ClockProfileCatalog catalog = Discover(root.Path);
        string[] ids = catalog.Profiles.Select(profile => profile.Id).ToArray();
        string[] labels = catalog.Profiles.Select(profile => profile.UiLabel).ToArray();

        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
        Assert.Equal(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog.Profiles, profile => Assert.Contains(profile.Id, profile.UiLabel));
    }

    [Fact]
    public void MissingProfile_DoesNotFallback()
    {
        ClockProfileCatalog catalog = ClockProfileCatalog.CreateDefault();

        KeyNotFoundException exception =
            Assert.Throws<KeyNotFoundException>(() => catalog.Get("league-replay-v999"));

        Assert.Contains("unavailable", exception.Message);
    }

    [Fact]
    public void DefaultProfile_IsHighestNumericVersionInCompatibleFamily()
    {
        using TemporaryDirectory root = new();
        WriteProfile(root.Path, "v9", "league-replay-v9", includeImage: true, version: 9);
        WriteProfile(root.Path, "v10", "league-replay-v10", includeImage: true, version: 10);
        WriteProfile(root.Path, "v2-copy", "other-family-v2", includeImage: true, version: 2);

        ClockProfileCatalog catalog = Discover(root.Path);

        Assert.Equal("league-replay-v10", catalog.DefaultProfile.Id);
        Assert.Equal(
            ["league-replay-v10", "league-replay-v9", "league-replay-v2"],
            catalog.Profiles
                .Where(profile => profile.Family == "league-replay")
                .OrderByDescending(profile => profile.Version)
                .Take(3)
                .Select(profile => profile.Id)
                .ToArray());
        Assert.Contains(catalog.Errors, error =>
            error.Message.Contains("family 'other-family' is incompatible", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedOrMismatchedNumericVersion_IsRejectedClearly()
    {
        using TemporaryDirectory root = new();
        WriteProfile(
            root.Path,
            "mismatch",
            "league-replay-v10",
            includeImage: true,
            version: 9);

        ClockProfileCatalog catalog = Discover(root.Path);

        Assert.False(catalog.TryGet("league-replay-v10", out _));
        Assert.Contains(catalog.Errors, error =>
            error.Message.Contains("declares version 9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TemplateBackedProfile_WithUnavailableAssets_DoesNotFallbackToSynthetic()
    {
        ClockRecognitionProfile unavailable = BuiltInClockProfiles.All
            .Single(profile => profile.Id == BuiltInClockProfiles.LeagueReplayV2Id) with
        {
            Id = "uninstalled-template-profile"
        };
        using ClockImage image = ClockTestImages.Render("0:00");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () => await new ConstrainedClockImageRecognizer()
                .RecognizeAsync(image, unavailable));

        Assert.Contains("requires installed template assets", exception.Message);
    }

    [Fact]
    public async Task PersistedV3StableId_ReloadsExactly()
    {
        using TemporaryDirectory root = new();
        JsonCaptureLayoutStore store = new(root.Path);
        CaptureLayout layout = new(
            "v3-layout",
            new NormalizedRegion(0.1, 0.1, 0.2, 0.2),
            new NormalizedRegion(0.6, 0.6, 0.2, 0.2),
            16d / 9,
            "league-replay-v3");

        await store.SaveAsync(layout, overwrite: false);
        CaptureLayout loaded = await store.LoadAsync(layout.Name);

        Assert.Equal("league-replay-v3", loaded.ClockProfileId);
        Assert.Equal("league-replay-v3", ClockProfileCatalog.CreateDefault()
            .Get(loaded.ClockProfileId!).Id);
    }

    [Fact]
    public void PackagedDiscovery_DoesNotDependOnRepositoryWorkingDirectory()
    {
        string original = Environment.CurrentDirectory;
        using TemporaryDirectory unrelated = new();
        try
        {
            Environment.CurrentDirectory = unrelated.Path;

            ClockProfileCatalog catalog = ClockProfileCatalog.CreateDefault();

            Assert.Equal(135, catalog.Get("league-replay-v3").TemplateCount);
            Assert.StartsWith(
                Path.GetFullPath(AppContext.BaseDirectory),
                Path.GetFullPath(catalog.Get("league-replay-v3").SourceManifestPath!),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    private static ClockProfileCatalog Discover(string root) =>
        ClockProfileCatalog.Discover([
            new ClockProfileSearchRoot(root, ClockProfileProvenance.UserInstalled)
        ]);

    private static void WriteProfile(
        string root,
        string directoryName,
        string profileId,
        bool includeImage,
        int version = 1)
    {
        string directory = Path.Combine(root, directoryName);
        Directory.CreateDirectory(directory);
        ClockGlyphTemplateEntry entry = new(
            "zero",
            "0",
            "templates/zero.pgm",
            new ClockGlyphProvenance(
                "sample",
                "0:00",
                0,
                "0",
                $"{profileId}/test"));
        ClockTemplateManifest manifest = new(
            1,
            profileId,
            version,
            BuiltInClockProfiles.LeagueReplayV1Id,
            1,
            1,
            "test",
            [entry],
            []);
        ClockTemplateProfileLoader.WriteManifest(directory, manifest);
        if (includeImage)
        {
            Directory.CreateDirectory(Path.Combine(directory, "templates"));
            ClockTemplateProfileLoader.WriteBinaryPgm(
                Path.Combine(directory, entry.Image),
                [true],
                1,
                1);
        }
    }
}

[CollectionDefinition("Clock profile discovery", DisableParallelization = true)]
public sealed class ClockProfileDiscoveryCollection;
