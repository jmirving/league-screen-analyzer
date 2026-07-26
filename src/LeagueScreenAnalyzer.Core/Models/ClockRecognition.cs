namespace LeagueScreenAnalyzer.Core.Models;

public interface IClockImagePayload : IFramePayload
{
    ReadOnlyMemory<byte> BgraPixels { get; }

    int Stride { get; }
}

public enum ClockForegroundPolarity
{
    LightOnDark,
    DarkOnLight
}

public enum ClockThresholdStrategy
{
    Fixed,
    Otsu
}

public enum ClockValidationMode
{
    ReplayContinuous,
    BroadcastVod
}

public sealed record ClockImage(
    int Width,
    int Height,
    int Stride,
    ReadOnlyMemory<byte> BgraPixels,
    long SourceFrameSequence,
    TimeSpan SourceTimestamp,
    IDisposable? Owner = null) : IDisposable
{
    private int _disposed;

    public ClockImage Validate()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        if (Stride < Width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(Stride));
        }

        if (BgraPixels.Length < Stride * Height)
        {
            throw new ArgumentException("Pixel buffer is smaller than the described image.", nameof(BgraPixels));
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

public sealed record ClockCharacterCandidate(
    char Character,
    double Confidence,
    double Margin = 0,
    string? TemplateSource = null);

public sealed record ClockCandidate(
    string Text,
    TimeSpan? ParsedGameTime,
    double Confidence,
    IReadOnlyList<ClockCharacterCandidate> Characters,
    string? Diagnostic = null);

public sealed record ClockRecognitionDiagnostics(
    int NormalizedWidth,
    int NormalizedHeight,
    byte[] NormalizedPixels,
    IReadOnlyList<ClockSegment> Segments,
    string PreprocessingVariant,
    string? Reason = null);

public sealed record ClockSegment(int X, int Y, int Width, int Height, byte[] Pixels);

public sealed record ClockRecognitionResult(
    IReadOnlyList<ClockCandidate> Candidates,
    ClockReadingStatus Status,
    double Confidence,
    string? DiagnosticReason,
    ClockRecognitionDiagnostics Diagnostics)
{
    public ClockCandidate? BestCandidate => Candidates.Count == 0 ? null : Candidates[0];
}

public sealed record ClockRecognitionProfile(
    string Id,
    string Name,
    int Version,
    string ExpectedPattern,
    int MinimumCharacterCount,
    int MaximumCharacterCount,
    ClockForegroundPolarity ForegroundPolarity,
    ClockThresholdStrategy ThresholdStrategy,
    byte FixedThreshold,
    double MinimumRecognitionConfidence,
    TimeSpan MaximumGameTime,
    double PlaybackSpeed,
    TimeSpan ForwardTolerance,
    TimeSpan BackwardTolerance,
    TimeSpan LongMissingInterval,
    double MaximumSamplesPerSecond,
    ClockValidationMode ValidationMode = ClockValidationMode.ReplayContinuous)
{
    public ClockRecognitionProfile Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ExpectedPattern);
        if (Version <= 0 || MinimumCharacterCount <= 0 || MaximumCharacterCount < MinimumCharacterCount)
        {
            throw new ArgumentException("Profile version and character bounds must be positive and ordered.");
        }

        if (MinimumRecognitionConfidence is < 0 or > 1 ||
            !double.IsFinite(PlaybackSpeed) || PlaybackSpeed <= 0 ||
            !double.IsFinite(MaximumSamplesPerSecond) || MaximumSamplesPerSecond <= 0 ||
            MaximumGameTime <= TimeSpan.Zero ||
            ForwardTolerance < TimeSpan.Zero ||
            BackwardTolerance < TimeSpan.Zero ||
            LongMissingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Profile numeric values are invalid.");
        }

        return this;
    }

    public ClockRecognitionProfile WithPlaybackSpeed(double playbackSpeed) =>
        this with { PlaybackSpeed = playbackSpeed };
}

public sealed record ClockTemporalContext(
    TimeSpan? LastAcceptedGameTime,
    TimeSpan? LastAcceptedSourceTimestamp,
    TimeSpan? LastObservationSourceTimestamp,
    TimeSpan? UnavailableSinceSourceTimestamp);
