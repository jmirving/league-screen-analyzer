using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Cli;

public sealed record MinimapFeatureDistribution(
    double Minimum,
    double Median,
    double Maximum);

public sealed record MinimapAnalysisReport(
    int TotalSamples,
    int ValidSamples,
    int InvalidSamples,
    int UncertainSamples,
    int UnlabeledSamples,
    IReadOnlyDictionary<string, MinimapFeatureDistribution> FeatureDistributions);

public sealed record MinimapSampleEvaluation(
    string Path,
    MinimapSampleLabel Label,
    MapFrameStatus Status,
    double Confidence,
    bool Accepted,
    IReadOnlyList<string> RejectionReasons);

public sealed record MinimapEvaluationReport(
    int TotalLabeledSamples,
    int ValidSamples,
    int InvalidSamples,
    int UncertainSamples,
    int TruePositives,
    int TrueNegatives,
    int FalsePositives,
    int FalseNegatives,
    double Precision,
    double Recall,
    IReadOnlyDictionary<string, MinimapFeatureDistribution> FeatureDistributions,
    IReadOnlyDictionary<string, int> RejectionReasons,
    IReadOnlyList<MinimapSampleEvaluation> Samples);

public sealed class MinimapCalibrationService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<MinimapAnalysisReport> AnalyzeAsync(
        string diagnosticsDirectory,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(string Path, MapDiagnosticSample Sample)> samples =
            await LoadSamplesAsync(diagnosticsDirectory, cancellationToken).ConfigureAwait(false);
        MinimapAnalysisReport report = new(
            samples.Count,
            samples.Count(value => value.Sample.Label == MinimapSampleLabel.Valid),
            samples.Count(value => value.Sample.Label == MinimapSampleLabel.Invalid),
            samples.Count(value => value.Sample.Label == MinimapSampleLabel.Uncertain),
            samples.Count(value => value.Sample.Label == MinimapSampleLabel.Unlabeled),
            Distributions(samples.Select(value => value.Sample.ValidatorResult.Features)));
        await WriteReportAsync(outputDirectory, "minimap-analysis.json", report, cancellationToken)
            .ConfigureAwait(false);
        return report;
    }

    public async Task<MinimapValidationProfile> BuildProfileAsync(
        string profileId,
        string diagnosticsDirectory,
        string outputProfileDirectory,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(string Path, MapDiagnosticSample Sample)> samples =
            await LoadSamplesAsync(diagnosticsDirectory, cancellationToken).ConfigureAwait(false);
        MapFeatureValues[] valid = samples
            .Where(value => value.Sample.Label == MinimapSampleLabel.Valid)
            .Select(value => value.Sample.ValidatorResult.Features)
            .OfType<MapFeatureValues>()
            .ToArray();
        int invalidCount = samples.Count(value => value.Sample.Label == MinimapSampleLabel.Invalid);
        if (valid.Length == 0)
        {
            throw new ArgumentException("At least one explicitly labeled valid minimap sample is required.");
        }

        MinimapValidationProfile baseline = BuiltInMinimapProfiles.LeagueReplayMinimapV1;
        double expectedAspect = Median(valid.Select(value => value.AspectRatio));
        MinimapValidationProfile profile = baseline with
        {
            Id = profileId,
            DisplayName = $"{profileId} labeled calibration",
            Version = 1,
            ExpectedAspectRatio = expectedAspect,
            AspectRatioTolerance = Math.Max(0.01, valid.Max(value =>
                Math.Abs(value.AspectRatio - expectedAspect) / expectedAspect) + 0.005),
            MinimumCropWidth = valid.Min(value => value.CropWidth),
            MinimumCropHeight = valid.Min(value => value.CropHeight),
            MinimumLuminanceVariance = valid.Min(value => value.LuminanceVariance) * 0.95,
            MinimumLuminanceSpread = valid.Min(value =>
                value.MaximumLuminance - value.MinimumLuminance) * 0.95,
            MaximumNearBlackPercentage = Math.Min(1, valid.Max(value => value.NearBlackPercentage) + 0.01),
            MaximumNearUniformPercentage = Math.Min(1, valid.Max(value => value.NearUniformPercentage) + 0.01),
            MinimumEdgeDensity = Math.Max(0, valid.Min(value => value.EdgeDensity) - 0.005),
            MaximumEdgeDensity = Math.Min(1, valid.Max(value => value.EdgeDensity) + 0.005),
            MinimumBorderConsistency = Math.Max(0, valid.Min(value => value.BorderConsistency) - 0.01),
            MinimumCornerConsistency = Math.Max(0, valid.Min(value => value.CornerConsistency) - 0.01),
            Provenance =
                $"Built deterministically from {valid.Length} explicitly valid and {invalidCount} explicitly invalid labeled diagnostics.",
            CalibratedForCanonicalRecording = valid.Length > 0 && invalidCount > 0
        };
        profile.Validate();
        Directory.CreateDirectory(outputProfileDirectory);
        await MinimapProfileSerializer.SaveAsync(
            profile,
            Path.Combine(outputProfileDirectory, "profile.json"),
            cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<MinimapEvaluationReport> EvaluateAsync(
        string profilePathOrId,
        string diagnosticsDirectory,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MinimapValidationProfile profile =
            MinimapProfileCatalog.CreateDefault().Resolve(profilePathOrId);
        IReadOnlyList<(string Path, MapDiagnosticSample Sample)> loaded =
            await LoadSamplesAsync(diagnosticsDirectory, cancellationToken).ConfigureAwait(false);
        List<MinimapSampleEvaluation> results = [];
        foreach ((string path, MapDiagnosticSample sample) in loaded)
        {
            (MapFrameStatus status, double confidence, IReadOnlyList<string> reasons) =
                EvaluateFeatures(profile, sample.ValidatorResult.Features);
            results.Add(new MinimapSampleEvaluation(
                path,
                sample.Label,
                status,
                confidence,
                status == MapFrameStatus.Valid,
                reasons));
        }

        MinimapSampleEvaluation[] primary = results
            .Where(value => value.Label is MinimapSampleLabel.Valid or MinimapSampleLabel.Invalid)
            .ToArray();
        int truePositive = primary.Count(value => value.Label == MinimapSampleLabel.Valid && value.Accepted);
        int trueNegative = primary.Count(value => value.Label == MinimapSampleLabel.Invalid && !value.Accepted);
        int falsePositive = primary.Count(value => value.Label == MinimapSampleLabel.Invalid && value.Accepted);
        int falseNegative = primary.Count(value => value.Label == MinimapSampleLabel.Valid && !value.Accepted);
        MinimapEvaluationReport report = new(
            primary.Length,
            primary.Count(value => value.Label == MinimapSampleLabel.Valid),
            primary.Count(value => value.Label == MinimapSampleLabel.Invalid),
            results.Count(value => value.Label == MinimapSampleLabel.Uncertain),
            truePositive,
            trueNegative,
            falsePositive,
            falseNegative,
            truePositive + falsePositive == 0 ? 0 : truePositive / (double)(truePositive + falsePositive),
            truePositive + falseNegative == 0 ? 0 : truePositive / (double)(truePositive + falseNegative),
            Distributions(loaded.Select(value => value.Sample.ValidatorResult.Features)),
            results.SelectMany(value => value.RejectionReasons)
                .GroupBy(value => value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            results);
        await WriteReportAsync(outputDirectory, "minimap-evaluation.json", report, cancellationToken)
            .ConfigureAwait(false);
        return report;
    }

    private static (MapFrameStatus Status, double Confidence, IReadOnlyList<string> Reasons)
        EvaluateFeatures(MinimapValidationProfile profile, MapFeatureValues? features)
    {
        if (features is null)
        {
            return (MapFrameStatus.Unknown, 0, new[] { "Required feature values are unavailable." });
        }

        List<string> reasons = [];
        RegionGeometryValidation geometry = new SemanticRegionShapePolicy().Validate(
            RegionType.Minimap,
            new NormalizedRegion(0, 0, 1, 1),
            new RegionSourceSize(features.CropWidth, features.CropHeight));
        if (features.CropWidth < profile.MinimumCropWidth ||
            features.CropHeight < profile.MinimumCropHeight ||
            !geometry.IsValid)
        {
            return (MapFrameStatus.IncompatibleGeometry, 0, new[]
            {
                geometry.Error ?? "Crop geometry is incompatible with the profile."
            });
        }

        if (features.NearBlackPercentage > profile.MaximumNearBlackPercentage)
        {
            return (MapFrameStatus.Obscured, 0, new[] { "Near-black pixel percentage exceeds the profile maximum." });
        }

        if (features.LuminanceVariance < profile.MinimumLuminanceVariance ||
            features.MaximumLuminance - features.MinimumLuminance < profile.MinimumLuminanceSpread ||
            features.NearUniformPercentage > profile.MaximumNearUniformPercentage)
        {
            return (MapFrameStatus.LowInformation, 0, new[] { "Crop does not contain enough luminance information." });
        }

        if (features.EdgeDensity < profile.MinimumEdgeDensity ||
            features.EdgeDensity > profile.MaximumEdgeDensity)
        {
            reasons.Add("Edge density is outside the profile range.");
        }

        if (features.BorderConsistency < profile.MinimumBorderConsistency)
        {
            reasons.Add("Border structure is inconsistent with the profile.");
        }

        if (features.CornerConsistency < profile.MinimumCornerConsistency)
        {
            reasons.Add("Corner structure is inconsistent with the profile.");
        }

        double confidence = reasons.Count == 0 ? 1 : 0;
        return reasons.Count == 0 && confidence >= profile.MinimumConfidence
            ? (MapFrameStatus.Valid, confidence, Array.Empty<string>())
            : (reasons.Count == 0 ? MapFrameStatus.LowConfidence : MapFrameStatus.Misaligned, confidence, reasons);
    }

    private static async Task<IReadOnlyList<(string Path, MapDiagnosticSample Sample)>> LoadSamplesAsync(
        string diagnosticsDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(diagnosticsDirectory))
        {
            throw new ArgumentException($"Diagnostics directory does not exist: {diagnosticsDirectory}");
        }

        List<(string, MapDiagnosticSample)> samples = [];
        foreach (string path in Directory.EnumerateFiles(
                     diagnosticsDirectory,
                     "result.json",
                     SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            await using FileStream stream = File.OpenRead(path);
            MapDiagnosticSample? sample = await JsonSerializer.DeserializeAsync<MapDiagnosticSample>(
                stream,
                Options,
                cancellationToken).ConfigureAwait(false);
            if (sample is not null)
            {
                samples.Add((path, sample));
            }
        }

        return samples;
    }

    private static IReadOnlyDictionary<string, MinimapFeatureDistribution> Distributions(
        IEnumerable<MapFeatureValues?> featureValues)
    {
        MapFeatureValues[] values = featureValues.OfType<MapFeatureValues>().ToArray();
        return new Dictionary<string, MinimapFeatureDistribution>(StringComparer.Ordinal)
        {
            ["meanLuminance"] = Distribution(values.Select(value => value.MeanLuminance)),
            ["luminanceVariance"] = Distribution(values.Select(value => value.LuminanceVariance)),
            ["nearBlackPercentage"] = Distribution(values.Select(value => value.NearBlackPercentage)),
            ["nearUniformPercentage"] = Distribution(values.Select(value => value.NearUniformPercentage)),
            ["edgeDensity"] = Distribution(values.Select(value => value.EdgeDensity)),
            ["borderConsistency"] = Distribution(values.Select(value => value.BorderConsistency)),
            ["cornerConsistency"] = Distribution(values.Select(value => value.CornerConsistency))
        };
    }

    private static MinimapFeatureDistribution Distribution(IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        return values.Length == 0
            ? new MinimapFeatureDistribution(0, 0, 0)
            : new MinimapFeatureDistribution(values[0], values[values.Length / 2], values[^1]);
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        return values[values.Length / 2];
    }

    private static async Task WriteReportAsync<T>(
        string outputDirectory,
        string fileName,
        T report,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await using FileStream stream = File.Create(Path.Combine(outputDirectory, fileName));
        await JsonSerializer.SerializeAsync(stream, report, Options, cancellationToken).ConfigureAwait(false);
    }
}
