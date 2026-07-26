namespace LeagueScreenAnalyzer.Core.Models;

public enum SessionMode
{
    ReplayContinuous,
    BroadcastVod
}

public enum MinimapSampleLabel
{
    Valid,
    Invalid,
    Uncertain,
    Unlabeled
}

public sealed record MapImage(
    int Width,
    int Height,
    int Stride,
    ReadOnlyMemory<byte> BgraPixels,
    long SourceFrameSequence,
    TimeSpan SourceTimestamp,
    IDisposable? Owner = null) : IDisposable
{
    private int _disposed;

    public MapImage Validate()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        if (Stride < Width * 4 || BgraPixels.Length < Stride * Height)
        {
            throw new ArgumentException("Pixel buffer is smaller than the described BGRA image.");
        }

        return this;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Owner?.Dispose();
        }
    }
}

public sealed record MapFeatureValues(
    int CropWidth,
    int CropHeight,
    double AspectRatio,
    double MeanLuminance,
    double LuminanceVariance,
    byte MinimumLuminance,
    byte MaximumLuminance,
    double NearBlackPercentage,
    double NearUniformPercentage,
    double EdgeDensity,
    double BorderConsistency,
    double CornerConsistency,
    double? ReferenceSimilarity = null);

public sealed record MinimapValidationProfile(
    string Id,
    string DisplayName,
    int Version,
    SessionMode TargetMode,
    double ExpectedAspectRatio,
    double AspectRatioTolerance,
    int NormalizedWidth,
    int NormalizedHeight,
    int MinimumCropWidth,
    int MinimumCropHeight,
    double MinimumLuminanceVariance,
    double MinimumLuminanceSpread,
    double MaximumNearBlackPercentage,
    double MaximumNearUniformPercentage,
    double MinimumEdgeDensity,
    double MaximumEdgeDensity,
    double MinimumBorderConsistency,
    double MinimumCornerConsistency,
    double MinimumConfidence,
    string Provenance,
    bool CalibratedForCanonicalRecording = false)
{
    public MinimapValidationProfile Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provenance);
        if (Version <= 0 || ExpectedAspectRatio <= 0 || AspectRatioTolerance < 0 ||
            NormalizedWidth <= 0 || NormalizedHeight <= 0 ||
            MinimumCropWidth <= 0 || MinimumCropHeight <= 0 ||
            MinimumLuminanceVariance < 0 || MinimumLuminanceSpread < 0 ||
            !InUnitRange(MaximumNearBlackPercentage) ||
            !InUnitRange(MaximumNearUniformPercentage) ||
            !InUnitRange(MinimumEdgeDensity) ||
            !InUnitRange(MaximumEdgeDensity) ||
            MinimumEdgeDensity > MaximumEdgeDensity ||
            !InUnitRange(MinimumBorderConsistency) ||
            !InUnitRange(MinimumCornerConsistency) ||
            !InUnitRange(MinimumConfidence))
        {
            throw new ArgumentException("Minimap profile contains invalid or unordered thresholds.");
        }

        return this;
    }

    private static bool InUnitRange(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;
}

public sealed record MapDiagnosticSample(
    string SchemaVersion,
    MinimapSampleLabel Label,
    string ProfileId,
    int ProfileVersion,
    MapValidationResult ValidatorResult,
    long SourceFrameSequence,
    TimeSpan SourceTimestamp,
    TimeSpan? AcceptedGameTime,
    int CropWidth,
    int CropHeight,
    string? CaptureLayout,
    string OriginalImagePath,
    string ProcessedImagePath);
