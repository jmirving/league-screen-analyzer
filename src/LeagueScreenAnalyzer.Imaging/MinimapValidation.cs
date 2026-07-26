using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Imaging;

public sealed class MinimapFeatureExtractor
{
    private const byte NearBlackThreshold = 16;
    private const byte UniformTolerance = 4;
    private const byte EdgeThreshold = 24;

    public MapFeatureValues Extract(MapImage image)
    {
        image.Validate();
        ReadOnlySpan<byte> pixels = image.BgraPixels.Span;
        int count = checked(image.Width * image.Height);
        double sum = 0;
        double squareSum = 0;
        int nearBlack = 0;
        byte minimum = byte.MaxValue;
        byte maximum = byte.MinValue;
        byte[] luminance = new byte[count];

        for (int y = 0; y < image.Height; y++)
        {
            int sourceRow = y * image.Stride;
            int targetRow = y * image.Width;
            for (int x = 0; x < image.Width; x++)
            {
                int offset = sourceRow + (x * 4);
                byte value = (byte)Math.Clamp(
                    ((pixels[offset + 2] * 54) +
                     (pixels[offset + 1] * 183) +
                     (pixels[offset] * 19) + 128) >> 8,
                    0,
                    255);
                luminance[targetRow + x] = value;
                sum += value;
                squareSum += value * value;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                if (value <= NearBlackThreshold)
                {
                    nearBlack++;
                }
            }
        }

        double mean = sum / count;
        double variance = Math.Max(0, (squareSum / count) - (mean * mean));
        int nearUniform = 0;
        foreach (byte value in luminance)
        {
            if (Math.Abs(value - mean) <= UniformTolerance)
            {
                nearUniform++;
            }
        }

        long edgeCount = 0;
        long edgeComparisons = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                byte value = luminance[(y * image.Width) + x];
                if (x + 1 < image.Width)
                {
                    edgeCount += Math.Abs(value - luminance[(y * image.Width) + x + 1]) >= EdgeThreshold ? 1 : 0;
                    edgeComparisons++;
                }

                if (y + 1 < image.Height)
                {
                    edgeCount += Math.Abs(value - luminance[((y + 1) * image.Width) + x]) >= EdgeThreshold ? 1 : 0;
                    edgeComparisons++;
                }
            }
        }

        double borderConsistency = Consistency(BorderPixels(luminance, image.Width, image.Height));
        double cornerConsistency = Consistency(CornerPixels(luminance, image.Width, image.Height));
        return new MapFeatureValues(
            image.Width,
            image.Height,
            image.Width / (double)image.Height,
            mean,
            variance,
            minimum,
            maximum,
            nearBlack / (double)count,
            nearUniform / (double)count,
            edgeComparisons == 0 ? 0 : edgeCount / (double)edgeComparisons,
            borderConsistency,
            cornerConsistency);
    }

    public static byte[] NormalizeGrayscale(MapImage image, int width, int height)
    {
        image.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ReadOnlySpan<byte> source = image.BgraPixels.Span;
        byte[] normalized = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(image.Height - 1, (y * image.Height) / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(image.Width - 1, (x * image.Width) / width);
                int offset = (sourceY * image.Stride) + (sourceX * 4);
                normalized[(y * width) + x] = (byte)(
                    ((source[offset + 2] * 54) +
                     (source[offset + 1] * 183) +
                     (source[offset] * 19) + 128) >> 8);
            }
        }

        return normalized;
    }

    private static IEnumerable<byte> BorderPixels(byte[] pixels, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            yield return pixels[x];
            yield return pixels[((height - 1) * width) + x];
        }

        for (int y = 1; y < height - 1; y++)
        {
            yield return pixels[y * width];
            yield return pixels[(y * width) + width - 1];
        }
    }

    private static IEnumerable<byte> CornerPixels(byte[] pixels, int width, int height)
    {
        int sampleWidth = Math.Max(1, width / 8);
        int sampleHeight = Math.Max(1, height / 8);
        foreach (int originX in new[] { 0, width - sampleWidth })
        foreach (int originY in new[] { 0, height - sampleHeight })
        for (int y = originY; y < originY + sampleHeight; y++)
        for (int x = originX; x < originX + sampleWidth; x++)
        {
            yield return pixels[(y * width) + x];
        }
    }

    private static double Consistency(IEnumerable<byte> values)
    {
        double[] samples = values.Select(value => (double)value).ToArray();
        if (samples.Length == 0)
        {
            return 0;
        }

        double mean = samples.Average();
        double standardDeviation = Math.Sqrt(samples.Average(value => Math.Pow(value - mean, 2)));
        return Math.Clamp(1 - (standardDeviation / 128), 0, 1);
    }
}

public sealed class StructuralMinimapValidator(
    MinimapValidationProfile profile,
    MinimapFeatureExtractor? featureExtractor = null) : IMapImageValidator, IMapFrameValidator
{
    private readonly MinimapValidationProfile _profile =
        (profile ?? throw new ArgumentNullException(nameof(profile))).Validate();
    private readonly MinimapFeatureExtractor _featureExtractor = featureExtractor ?? new();

    public MinimapValidationProfile Profile => _profile;

    public ValueTask<MapValidationResult> ValidateAsync(
        MapImage minimapImage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(minimapImage);
        cancellationToken.ThrowIfCancellationRequested();
        MapFeatureValues features = _featureExtractor.Extract(minimapImage);
        List<string> reasons = [];

        RegionGeometryValidation semanticGeometry =
            new SemanticRegionShapePolicy().Validate(
                RegionType.Minimap,
                new NormalizedRegion(0, 0, 1, 1),
                new RegionSourceSize(features.CropWidth, features.CropHeight));
        if (features.CropWidth < _profile.MinimumCropWidth ||
            features.CropHeight < _profile.MinimumCropHeight ||
            !semanticGeometry.IsValid)
        {
            reasons.Add(
                semanticGeometry.Error ??
                "Crop geometry is incompatible with the profile.");
            return ValueTask.FromResult(
                Result(MapFrameStatus.IncompatibleGeometry, 0, reasons, features, minimapImage));
        }

        double spread = features.MaximumLuminance - features.MinimumLuminance;
        if (features.NearBlackPercentage > _profile.MaximumNearBlackPercentage)
        {
            reasons.Add("Near-black pixel percentage exceeds the profile maximum.");
            return ValueTask.FromResult(
                Result(MapFrameStatus.Obscured, 0, reasons, features, minimapImage));
        }

        if (features.LuminanceVariance < _profile.MinimumLuminanceVariance ||
            spread < _profile.MinimumLuminanceSpread ||
            features.NearUniformPercentage > _profile.MaximumNearUniformPercentage)
        {
            reasons.Add("Crop does not contain enough luminance information.");
            return ValueTask.FromResult(
                Result(MapFrameStatus.LowInformation, 0, reasons, features, minimapImage));
        }

        if (features.EdgeDensity < _profile.MinimumEdgeDensity ||
            features.EdgeDensity > _profile.MaximumEdgeDensity)
        {
            reasons.Add("Edge density is outside the profile range.");
        }

        if (features.BorderConsistency < _profile.MinimumBorderConsistency)
        {
            reasons.Add("Border structure is inconsistent with the profile.");
        }

        if (features.CornerConsistency < _profile.MinimumCornerConsistency)
        {
            reasons.Add("Corner structure is inconsistent with the profile.");
        }

        double confidence = Math.Clamp(Math.Min(
            RangeScore(features.EdgeDensity, _profile.MinimumEdgeDensity, _profile.MaximumEdgeDensity),
            Math.Min(
                ThresholdScore(features.BorderConsistency, _profile.MinimumBorderConsistency),
                ThresholdScore(features.CornerConsistency, _profile.MinimumCornerConsistency))),
            0,
            1);
        if (reasons.Count > 0)
        {
            return ValueTask.FromResult(
                Result(MapFrameStatus.Misaligned, confidence, reasons, features, minimapImage));
        }

        if (confidence < _profile.MinimumConfidence)
        {
            reasons.Add("Combined structural confidence is below the profile threshold.");
            return ValueTask.FromResult(
                Result(MapFrameStatus.LowConfidence, confidence, reasons, features, minimapImage));
        }

        return ValueTask.FromResult(
            Result(MapFrameStatus.Valid, confidence, [], features, minimapImage));
    }

    public ValueTask<MapValidationResult> ValidateAsync(
        RegionFrame minimapFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(minimapFrame);
        if (minimapFrame.RegionType != RegionType.Minimap)
        {
            throw new ArgumentException("The supplied region is not a minimap region.", nameof(minimapFrame));
        }

        if (minimapFrame.Payload is not IClockImagePayload payload)
        {
            return ValueTask.FromResult(new MapValidationResult(
                MapFrameStatus.Unknown,
                0,
                ["Required BGRA pixels are unavailable."],
                _profile.Id,
                sourceFrameSequence: minimapFrame.SourceFrameSequence,
                sourceTimestamp: minimapFrame.SourceTimestamp));
        }

        MapImage image = new(
            minimapFrame.Width,
            minimapFrame.Height,
            payload.Stride,
            payload.BgraPixels,
            minimapFrame.SourceFrameSequence,
            minimapFrame.SourceTimestamp);
        return ValidateAsync(image, cancellationToken);
    }

    private MapValidationResult Result(
        MapFrameStatus status,
        double confidence,
        IReadOnlyList<string> reasons,
        MapFeatureValues features,
        MapImage image) =>
        new(
            status,
            confidence,
            reasons,
            _profile.Id,
            features,
            image.SourceFrameSequence,
            image.SourceTimestamp);

    private static double ThresholdScore(double value, double minimum) =>
        minimum >= 1 ? (value >= minimum ? 1 : 0) : Math.Clamp((value - minimum) / (1 - minimum), 0, 1);

    private static double RangeScore(double value, double minimum, double maximum)
    {
        if (value < minimum || value > maximum)
        {
            return 0;
        }

        double midpoint = (minimum + maximum) / 2;
        double halfRange = Math.Max(0.000001, (maximum - minimum) / 2);
        return Math.Clamp(1 - (Math.Abs(value - midpoint) / (halfRange * 2)), 0, 1);
    }
}

public static class BuiltInMinimapProfiles
{
    public const string LeagueReplayMinimapV1Id = "league-replay-minimap-v1";

    public static MinimapValidationProfile LeagueReplayMinimapV1 { get; } =
        new MinimapValidationProfile(
        LeagueReplayMinimapV1Id,
        "League Replay Minimap (calibration)",
        1,
        SessionMode.ReplayContinuous,
        1,
        0.08,
        128,
        128,
        96,
        96,
        120,
        45,
        0.72,
        0.72,
        0.025,
        0.48,
        0.20,
        0.18,
        0.55,
        "Deterministic synthetic structural calibration; requires explicitly labeled real replay samples.",
        false).Validate();
}

public static class MinimapProfileSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task SaveAsync(
        MinimapValidationProfile profile,
        string path,
        CancellationToken cancellationToken = default)
    {
        profile.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<MinimapValidationProfile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(path);
        MinimapValidationProfile? profile =
            await JsonSerializer.DeserializeAsync<MinimapValidationProfile>(
                stream,
                Options,
                cancellationToken).ConfigureAwait(false);
        return (profile ?? throw new InvalidDataException("Minimap profile is empty.")).Validate();
    }

    public static MinimapValidationProfile Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        MinimapValidationProfile? profile =
            JsonSerializer.Deserialize<MinimapValidationProfile>(stream, Options);
        return (profile ?? throw new InvalidDataException("Minimap profile is empty.")).Validate();
    }
}
