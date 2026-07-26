namespace LeagueScreenAnalyzer.Core.Models;

public enum RegionType
{
    Clock,
    Minimap
}

public enum ClockReadingStatus
{
    Valid,
    NotConfigured,
    NotVisible,
    Unreadable,
    Malformed,
    LowConfidence,
    Implausible,
    Backward,
    Discontinuous,
    Unparseable,
    RejectedJump
}

public enum ClockTemporalStatus
{
    NotEvaluated,
    Accepted,
    Rejected
}

public enum MapFrameStatus
{
    Valid,
    NotConfigured,
    Missing,
    Obscured,
    Misaligned,
    LowInformation,
    LowConfidence,
    IncompatibleGeometry,
    Unknown
}

public enum ObservationStatus
{
    Valid,
    Unavailable
}

public sealed record ClockReading
{
    public ClockReading(
        TimeSpan? gameTime,
        double confidence,
        ClockReadingStatus status,
        string? diagnosticReason = null,
        string? rawRecognizedText = null,
        ClockCandidate? bestCandidate = null,
        double? imageRecognitionConfidence = null,
        ClockTemporalStatus temporalStatus = ClockTemporalStatus.NotEvaluated,
        long? sourceFrameSequence = null,
        TimeSpan? sourceTimestamp = null,
        TimeSpan? lastAcceptedGameTime = null,
        TimeSpan? lastAcceptedSourceTimestamp = null)
    {
        ValidateConfidence(confidence);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (gameTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gameTime), gameTime, "Game time cannot be negative.");
        }

        if (status == ClockReadingStatus.Valid && gameTime is null)
        {
            throw new ArgumentException("A valid clock reading requires a game time.", nameof(gameTime));
        }

        if (status != ClockReadingStatus.Valid && gameTime is not null)
        {
            throw new ArgumentException("An invalid clock reading cannot contain a game time.", nameof(gameTime));
        }

        GameTime = gameTime;
        Confidence = confidence;
        Status = status;
        DiagnosticReason = diagnosticReason;
        RawRecognizedText = rawRecognizedText;
        BestCandidate = bestCandidate;
        ImageRecognitionConfidence = imageRecognitionConfidence ?? confidence;
        ValidateConfidence(ImageRecognitionConfidence);
        TemporalStatus = temporalStatus;
        SourceFrameSequence = sourceFrameSequence;
        SourceTimestamp = sourceTimestamp;
        LastAcceptedGameTime = status == ClockReadingStatus.Valid ? gameTime : lastAcceptedGameTime;
        LastAcceptedSourceTimestamp = status == ClockReadingStatus.Valid ? sourceTimestamp : lastAcceptedSourceTimestamp;
    }

    public TimeSpan? GameTime { get; }

    public double Confidence { get; }

    public ClockReadingStatus Status { get; }

    public string? DiagnosticReason { get; }

    public string? RawRecognizedText { get; }

    public ClockCandidate? BestCandidate { get; }

    public double ImageRecognitionConfidence { get; }

    public ClockTemporalStatus TemporalStatus { get; }

    public long? SourceFrameSequence { get; }

    public TimeSpan? SourceTimestamp { get; }

    public TimeSpan? LastAcceptedGameTime { get; }

    public TimeSpan? LastAcceptedSourceTimestamp { get; }

    private static void ValidateConfidence(double confidence)
    {
        if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be finite and between 0 and 1.");
        }
    }
}

public sealed record MapValidationResult
{
    public MapValidationResult(
        MapFrameStatus status,
        double confidence,
        IReadOnlyList<string> reasons,
        string? profileId = null,
        MapFeatureValues? features = null,
        long? sourceFrameSequence = null,
        TimeSpan? sourceTimestamp = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be finite and between 0 and 1.");
        }

        Status = status;
        Confidence = confidence;
        Reasons = reasons?.ToArray() ?? throw new ArgumentNullException(nameof(reasons));
        ProfileId = profileId;
        Features = features;
        SourceFrameSequence = sourceFrameSequence;
        SourceTimestamp = sourceTimestamp;

        if (status == MapFrameStatus.Valid &&
            (string.IsNullOrWhiteSpace(profileId) || features is null))
        {
            throw new ArgumentException(
                "A valid minimap result requires a profile ID and complete feature values.",
                nameof(status));
        }
    }

    public MapFrameStatus Status { get; }

    public double Confidence { get; }

    public IReadOnlyList<string> Reasons { get; }

    public string? ProfileId { get; }

    public MapFeatureValues? Features { get; }

    public long? SourceFrameSequence { get; }

    public TimeSpan? SourceTimestamp { get; }
}

public sealed record TimelineObservation
{
    public TimelineObservation(
        TimeSpan sourceTimestamp,
        TimeSpan? gameTime,
        ObservationStatus status,
        ClockReading clockResult,
        MapValidationResult mapResult,
        string? mapArtifactPath = null,
        long? sourceFrameSequence = null,
        string? unavailabilityReason = null)
    {
        if (sourceTimestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceTimestamp));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentNullException.ThrowIfNull(clockResult);
        ArgumentNullException.ThrowIfNull(mapResult);

        bool evidenceIsValid =
            clockResult.Status == ClockReadingStatus.Valid &&
            mapResult.Status == MapFrameStatus.Valid;

        if (status == ObservationStatus.Valid &&
            (!evidenceIsValid || gameTime is null || gameTime != clockResult.GameTime))
        {
            throw new ArgumentException(
                "A valid observation requires valid clock and map evidence with matching game time.",
                nameof(status));
        }

        if (status == ObservationStatus.Unavailable && gameTime is not null)
        {
            throw new ArgumentException(
                "An unavailable observation cannot contain a game time.",
                nameof(gameTime));
        }

        if (status == ObservationStatus.Valid &&
            (sourceFrameSequence is null ||
             clockResult.SourceFrameSequence != sourceFrameSequence ||
             mapResult.SourceFrameSequence != sourceFrameSequence))
        {
            throw new ArgumentException(
                "A valid observation requires clock and minimap evidence from the same source frame.",
                nameof(sourceFrameSequence));
        }

        SourceTimestamp = sourceTimestamp;
        GameTime = gameTime;
        Status = status;
        ClockResult = clockResult;
        MapResult = mapResult;
        MapArtifactPath = mapArtifactPath;
        SourceFrameSequence = sourceFrameSequence;
        UnavailabilityReason = status == ObservationStatus.Unavailable
            ? unavailabilityReason ?? "clock-or-minimap-unavailable"
            : null;
    }

    public TimeSpan SourceTimestamp { get; }

    public TimeSpan? GameTime { get; }

    public ObservationStatus Status { get; }

    public ClockReading ClockResult { get; }

    public MapValidationResult MapResult { get; }

    public string? MapArtifactPath { get; }

    public long? SourceFrameSequence { get; }

    public string? UnavailabilityReason { get; }
}

public sealed record GapInterval
{
    public GapInterval(TimeSpan startGameTime, TimeSpan endGameTime, string reason)
    {
        if (startGameTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startGameTime));
        }

        if (endGameTime <= startGameTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endGameTime), "Gap end must be later than its start.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        StartGameTime = startGameTime;
        EndGameTime = endGameTime;
        Reason = reason;
    }

    public TimeSpan StartGameTime { get; }

    public TimeSpan EndGameTime { get; }

    public string Reason { get; }
}

public sealed record SessionSummary(
    int TotalFrames,
    int ValidObservations,
    int UnavailableObservations,
    TimeSpan? FirstGameTime,
    TimeSpan? LastGameTime,
    int DetectedGapCount,
    int RejectedClockCount);

public sealed record SessionProcessingResult(
    IReadOnlyList<TimelineObservation> Observations,
    IReadOnlyList<GapInterval> Gaps,
    SessionSummary Summary);
