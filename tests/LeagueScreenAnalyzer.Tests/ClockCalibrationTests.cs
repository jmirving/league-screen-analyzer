using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Cli;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockCalibrationTests
{
    [Fact]
    public void RealTemplateManifest_ParsesAndValidatesProvenance()
    {
        ClockTemplateManifest manifest = LoadV2Manifest();

        Assert.Equal("league-replay-v2", manifest.ProfileId);
        Assert.Equal(65, manifest.Templates.Count);
        Assert.All(manifest.Templates, entry =>
        {
            Assert.NotEmpty(entry.Provenance.SourceDiagnosticBundle);
            Assert.NotEmpty(entry.Provenance.ExplicitFullClockLabel);
            Assert.Equal(entry.Glyph, entry.Provenance.GlyphLabel);
            Assert.Contains("league-replay-v2", entry.Provenance.PreprocessingProfileVersion);
        });
    }

    [Fact]
    public void Manifest_RejectsDuplicateGlyphAssignment()
    {
        ClockGlyphTemplateEntry entry = ValidEntry("a", "a.pgm");
        ClockTemplateManifest manifest = ValidManifest([
            entry,
            entry with { TemplateId = "b", Image = "b.pgm" }
        ]);

        Assert.Throws<InvalidDataException>(() =>
            ClockTemplateProfileLoader.ValidateManifest(manifest, ".", requireImages: false));
    }

    [Fact]
    public void Manifest_RejectsMalformedProvenance()
    {
        ClockGlyphTemplateEntry entry = ValidEntry("a", "a.pgm") with
        {
            Provenance = new ClockGlyphProvenance("", "0:00", 0, "0", "v2")
        };

        Assert.Throws<InvalidDataException>(() =>
            ClockTemplateProfileLoader.ValidateManifest(
                ValidManifest([entry]), ".", requireImages: false));
    }

    [Fact]
    public void SegmentAlignment_SupportsSingleDigitMinuteLabelWithLeaguePadding() =>
        Assert.Equal("03:40", ClockCalibrationService.AlignDisplaySequence("3:40", 5));

    [Fact]
    public void SegmentAlignment_SupportsDoubleDigitMinuteLabel() =>
        Assert.Equal("10:00", ClockCalibrationService.AlignDisplaySequence("10:00", 5));

    [Fact]
    public void SegmentAlignment_AmbiguityFailsSafely() =>
        Assert.Throws<InvalidDataException>(() =>
            ClockCalibrationService.AlignDisplaySequence("3:40", 3));

    [Fact]
    public void RealProfile_ContainsReviewedColonTemplates()
    {
        ClockTemplateManifest manifest = LoadV2Manifest();
        ClockGlyphTemplateEntry[] separators =
            manifest.Templates.Where(entry => entry.Glyph == ":").ToArray();

        Assert.Equal(13, separators.Length);
        Assert.All(separators, entry => Assert.Equal(2, entry.Provenance.CharacterPosition));
    }

    [Fact]
    public void RealProfile_SupportsMultipleTemplatesPerDigit()
    {
        ClockTemplateManifest manifest = LoadV2Manifest();
        Assert.True(manifest.Templates.Count(entry => entry.Glyph == "0") > 1);
        Assert.True(manifest.Templates.Count(entry => entry.Glyph == "5") > 1);
    }

    [Fact]
    public void TranslationTolerance_PreservesNearMatch()
    {
        bool[] template = new bool[25];
        template[(2 * 5) + 2] = true;
        bool[] shifted = new bool[25];
        shifted[(2 * 5) + 3] = true;

        double exactOnly =
            ClockTemplateMatcher.SimilarityWithTranslation(shifted, template, 5, 5, 0);
        double tolerant =
            ClockTemplateMatcher.SimilarityWithTranslation(shifted, template, 5, 5, 1);

        Assert.True(tolerant > exactOnly);
        Assert.True(tolerant > 0.9);
    }

    [Fact]
    public void UnsupportedDigit_IsExplicitAndHasNoRealTemplate()
    {
        ClockTemplateManifest manifest = LoadV2Manifest();
        Assert.Contains("8", manifest.UnsupportedDigits);
        Assert.DoesNotContain(manifest.Templates, entry => entry.Glyph == "8");
    }

    [Fact]
    public void ProfileVersions_Coexist()
    {
        Assert.Equal(1, BuiltInClockProfiles.Get("league-replay-v1").Version);
        Assert.Equal(2, BuiltInClockProfiles.Get("league-replay-v2").Version);
        Assert.Equal(2, BuiltInClockProfiles.All.Count);
    }

    [Fact]
    public async Task Evaluation_ExcludesOwnTemplatesAndSeparatesVisualTemporalMetrics()
    {
        string root = FindRepositoryRoot();
        using TemporaryDirectory output = new();
        ClockEvaluationReport training =
            await new ClockEvaluationService().EvaluateDiagnosticBundlesAsync(
                "league-replay-v2",
                Path.Combine(root, "artifacts", "clock-samples"),
                output.Path);
        ClockEvaluationReport leaveOneOut = JsonSerializer.Deserialize<ClockEvaluationReport>(
            await File.ReadAllTextAsync(
                Path.Combine(output.Path, "clock-evaluation-leave-one-out.json")),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            })!;

        Assert.Equal("apparent-training-set", training.EvaluationKind);
        Assert.Equal("leave-one-sample-out", leaveOneOut.EvaluationKind);
        Assert.All(leaveOneOut.Samples, sample => Assert.True(sample.OwnTemplatesExcluded));
        Assert.Equal(0, leaveOneOut.FalseAccepts);
        Assert.Equal(leaveOneOut.FalseRejects, leaveOneOut.VisualRejections);
        Assert.True(leaveOneOut.VisualRejections > 0);
        Assert.Equal(0, leaveOneOut.TemporalRejections);
        Assert.Contains(
            leaveOneOut.Samples.SelectMany(sample => sample.BestCharacters),
            character => character.Margin > 0);
    }

    [Fact]
    public async Task IndependentEvaluation_HasNoTemporalCrossSampleContamination()
    {
        string root = FindRepositoryRoot();
        using TemporaryDirectory output = new();
        ClockEvaluationReport report =
            await new ClockEvaluationService().EvaluateDiagnosticBundlesAsync(
                "league-replay-v2",
                Path.Combine(root, "artifacts", "clock-samples"),
                output.Path);

        Assert.Equal(0, report.TemporalRejections);
        Assert.Equal(
            report.TotalSamples,
            report.CorrectlyAccepted + report.FalseAccepts + report.FalseRejects);
    }

    [Fact]
    public async Task ProfileGeneration_IsDeterministic()
    {
        string root = FindRepositoryRoot();
        using TemporaryDirectory first = new();
        using TemporaryDirectory second = new();
        ClockCalibrationService service = new();
        await service.BuildProfileAsync(
            "league-replay-v1", "league-replay-v2",
            Path.Combine(root, "artifacts", "clock-samples"), first.Path);
        await service.BuildProfileAsync(
            "league-replay-v1", "league-replay-v2",
            Path.Combine(root, "artifacts", "clock-samples"), second.Path);

        Assert.Equal(HashDirectory(first.Path), HashDirectory(second.Path));
    }

    [Fact]
    public async Task V1Diagnostics_CanBeEvaluatedWithV2AndV3_WithoutChangingProvenance()
    {
        string root = FindRepositoryRoot();
        string diagnostics = Path.Combine(root, "artifacts", "clock-samples");
        string sourceResult = Directory.EnumerateFiles(
                diagnostics, "result.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .First();
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceResult)));
        using TemporaryDirectory v2Output = new();
        using TemporaryDirectory v3Output = new();

        ClockEvaluationReport v2 = await new ClockEvaluationService()
            .EvaluateDiagnosticBundlesAsync("league-replay-v2", diagnostics, v2Output.Path);
        ClockEvaluationReport v3 = await new ClockEvaluationService()
            .EvaluateDiagnosticBundlesAsync("league-replay-v3", diagnostics, v3Output.Path);

        Assert.All(v2.Samples, sample =>
        {
            Assert.StartsWith("league-replay-v1 v", sample.CapturedWithProfile);
            Assert.Equal("league-replay-v2", sample.EvaluatedWithProfile);
        });
        Assert.All(v3.Samples, sample =>
        {
            Assert.StartsWith("league-replay-v1 v", sample.CapturedWithProfile);
            Assert.Equal("league-replay-v3", sample.EvaluatedWithProfile);
            Assert.NotNull(sample.OriginalCandidate);
            Assert.Equal(sample.RecognizedText, sample.NewCandidate);
        });
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceResult))));
    }

    [Fact]
    public async Task MixedCaptureProfiles_EvaluateDeterministicallyAndLeaveOneOut()
    {
        string root = FindRepositoryRoot();
        string diagnostics = Path.Combine(root, "artifacts", "clock-samples");
        string[] bundles = Directory.EnumerateFiles(
                diagnostics, "result.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(2)
            .Select(Path.GetDirectoryName)
            .Cast<string>()
            .ToArray();
        using TemporaryDirectory mixed = new();
        CopyBundle(bundles[0], Path.Combine(mixed.Path, "z-v1"));
        string v2Bundle = Path.Combine(mixed.Path, "a-v2");
        CopyBundle(bundles[1], v2Bundle);
        UpdateResult(v2Bundle, rootNode =>
        {
            rootNode["profile"]!["id"] = "league-replay-v2";
            rootNode["profile"]!["version"] = 2;
        });
        using TemporaryDirectory first = new();
        using TemporaryDirectory second = new();

        ClockEvaluationReport one = await new ClockEvaluationService()
            .EvaluateDiagnosticBundlesAsync("league-replay-v3", mixed.Path, first.Path);
        ClockEvaluationReport two = await new ClockEvaluationService()
            .EvaluateDiagnosticBundlesAsync("league-replay-v3", mixed.Path, second.Path);

        Assert.Equal(["a-v2", "z-v1"], one.Samples.Select(sample => sample.Id));
        Assert.Equal(
            ["league-replay-v2 v2", "league-replay-v1 v1"],
            one.Samples.Select(sample => sample.CapturedWithProfile));
        Assert.Equal(
            JsonSerializer.Serialize(one),
            JsonSerializer.Serialize(two));
        ClockEvaluationReport leaveOneOut = ReadEvaluation(
            Path.Combine(first.Path, "clock-evaluation-leave-one-out.json"));
        Assert.All(leaveOneOut.Samples, sample => Assert.True(sample.OwnTemplatesExcluded));
    }

    [Fact]
    public async Task V1Diagnostics_BuildFromV2IntoV3_UsingCurrentSegmentation()
    {
        string root = FindRepositoryRoot();
        using TemporaryDirectory output = new();

        ClockTemplateManifest manifest = await new ClockCalibrationService().BuildProfileAsync(
            "league-replay-v2",
            "test-league-replay-v3",
            Path.Combine(root, "artifacts", "clock-samples"),
            output.Path);

        Assert.Equal("league-replay-v2", manifest.BaseProfileId);
        Assert.Equal(3, manifest.ProfileVersion);
        Assert.All(manifest.Templates, template =>
        {
            Assert.Equal("league-replay-v1", template.Provenance.CapturedWithProfileId);
            Assert.Equal(1, template.Provenance.CapturedWithProfileVersion);
            Assert.Equal("test-league-replay-v3", template.Provenance.BuiltIntoProfile);
            Assert.Contains("Otsu/LightOnDark", template.Provenance.PreprocessingProfileVersion);
        });
        JsonElement compatibility = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(output.Path, "compatibility.json")))
            .RootElement;
        Assert.All(
            compatibility.EnumerateArray().Select(item => item.GetProperty("accepted").GetBoolean()),
            Assert.True);
    }

    [Fact]
    public async Task Evaluation_UsesExplicitLabelAsOnlyGroundTruth()
    {
        string source = FirstBundle();
        using TemporaryDirectory diagnostics = new();
        string bundle = Path.Combine(diagnostics.Path, "changed-label");
        CopyBundle(source, bundle);
        UpdateResult(bundle, rootNode => rootNode["explicitLabel"] = "99:59");
        using TemporaryDirectory output = new();

        ClockEvaluationReport report = await new ClockEvaluationService()
            .EvaluateDiagnosticBundlesAsync("league-replay-v3", diagnostics.Path, output.Path);

        ClockSampleEvaluation sample = Assert.Single(report.Samples);
        Assert.Equal("99:59", sample.Label);
        Assert.NotEqual(sample.Label, sample.NewCandidate);
        Assert.False(sample.ExactMatch);
        Assert.Equal(1, report.FalseAccepts);
    }

    [Fact]
    public async Task UnlabeledMalformedAndMissingCrop_AreRejectedWithReasons()
    {
        string source = FirstBundle();
        using TemporaryDirectory diagnostics = new();
        string unlabeled = Path.Combine(diagnostics.Path, "unlabeled");
        string malformed = Path.Combine(diagnostics.Path, "malformed");
        string missing = Path.Combine(diagnostics.Path, "missing");
        CopyBundle(source, unlabeled);
        CopyBundle(source, malformed);
        CopyBundle(source, missing, includeImage: false);
        UpdateResult(unlabeled, rootNode =>
        {
            rootNode["sampleKind"] = "unlabeledDiagnostic";
            rootNode["explicitLabel"] = null;
        });
        UpdateResult(malformed, rootNode => rootNode["explicitLabel"] = "8:99");
        using TemporaryDirectory output = new();

        ClockEvaluationReport report = await new ClockEvaluationService()
            .EvaluateDiagnosticBundlesAsync("league-replay-v3", diagnostics.Path, output.Path);

        Assert.Equal(0, report.TotalSamples);
        Assert.Equal(3, report.RejectedSamples);
        Assert.Contains(report.Compatibility, item =>
            item.Sample == "unlabeled" && item.RejectionReason!.Contains("unlabeled"));
        Assert.Contains(report.Compatibility, item =>
            item.Sample == "malformed" && item.RejectionReason!.Contains("Malformed explicit label"));
        Assert.Contains(report.Compatibility, item =>
            item.Sample == "missing" && item.RejectionReason!.Contains("missing"));
    }

    [Fact]
    public async Task TargetProfilePreprocessingFailure_IsReportedPerSample()
    {
        string source = FirstBundle();
        using TemporaryDirectory diagnostics = new();
        string bundle = Path.Combine(diagnostics.Path, "no-foreground");
        CopyBundle(source, bundle);
        string imagePath = Path.Combine(bundle, "original-clock.bmp");
        byte[] bytes = File.ReadAllBytes(imagePath);
        int pixelOffset = BitConverter.ToInt32(bytes, 10);
        Array.Clear(bytes, pixelOffset, bytes.Length - pixelOffset);
        File.WriteAllBytes(imagePath, bytes);
        using TemporaryDirectory output = new();

        ClockEvaluationReport report = await new ClockEvaluationService()
            .EvaluateDiagnosticBundlesAsync("league-replay-v3", diagnostics.Path, output.Path);

        ClockSampleEvaluation sample = Assert.Single(report.Samples);
        Assert.Equal(ClockReadingStatus.NotVisible, sample.NewStatus);
        Assert.Contains("luminance", sample.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, report.FalseRejects);
    }

    private static ClockEvaluationReport ReadEvaluation(string path) =>
        JsonSerializer.Deserialize<ClockEvaluationReport>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            })!;

    private static string FirstBundle() =>
        Path.GetDirectoryName(Directory.EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "artifacts", "clock-samples"),
                "result.json",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .First())!;

    private static void CopyBundle(string source, string destination, bool includeImage = true)
    {
        Directory.CreateDirectory(destination);
        File.Copy(Path.Combine(source, "result.json"), Path.Combine(destination, "result.json"));
        if (includeImage)
        {
            File.Copy(
                Path.Combine(source, "original-clock.bmp"),
                Path.Combine(destination, "original-clock.bmp"));
        }
    }

    private static void UpdateResult(string bundle, Action<JsonNode> update)
    {
        string path = Path.Combine(bundle, "result.json");
        JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
        update(root);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ClockTemplateManifest LoadV2Manifest()
    {
        string directory = Path.Combine(
            FindRepositoryRoot(), "fixtures", "clocks", "league-replay-v2");
        return ClockTemplateProfileLoader.LoadManifest(directory);
    }

    private static ClockTemplateManifest ValidManifest(
        IReadOnlyList<ClockGlyphTemplateEntry> entries) =>
        new(1, "test-v2", 2, "test-v1", 12, 16, "test", entries, ["8"]);

    private static ClockGlyphTemplateEntry ValidEntry(string id, string image) =>
        new(
            id,
            "0",
            image,
            new ClockGlyphProvenance("bundle", "0:00", 0, "0", "v2"));

    private static string HashDirectory(string directory)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(relative));
            hash.AppendData(File.ReadAllBytes(file));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "LeagueScreenAnalyzer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
               throw new InvalidOperationException("Repository root was not found.");
    }
}
