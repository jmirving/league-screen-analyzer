namespace LeagueScreenAnalyzer.Core.Models;

public sealed record SessionRecordingConfiguration(
    SessionMode Mode,
    TimeSpan RequestedGameTimeCadence,
    string CaptureLayout,
    string ClockProfileId,
    string MinimapProfileId,
    double PlaybackSpeed,
    int SourceWidth,
    int SourceHeight,
    string SourceType = "selected-window")
{
    public SessionRecordingConfiguration Validate()
    {
        if (!Enum.IsDefined(Mode) ||
            RequestedGameTimeCadence <= TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(CaptureLayout) ||
            string.IsNullOrWhiteSpace(ClockProfileId) ||
            string.IsNullOrWhiteSpace(MinimapProfileId) ||
            !double.IsFinite(PlaybackSpeed) ||
            PlaybackSpeed <= 0 ||
            SourceWidth <= 0 ||
            SourceHeight <= 0)
        {
            throw new ArgumentException("Session recording configuration is incomplete or invalid.");
        }

        return this;
    }
}

public sealed record SessionManifest(
    string SchemaVersion,
    string SessionId,
    string SourceType,
    SessionMode SessionMode,
    string CaptureLayout,
    string ClockProfileId,
    string MinimapProfileId,
    double PlaybackSpeed,
    TimeSpan RequestedGameTimeCadence,
    TimeSpan? StartSourceTimestamp,
    TimeSpan? EndSourceTimestamp,
    TimeSpan? FirstAcceptedGameTime,
    TimeSpan? LastAcceptedGameTime,
    int SourceWidth,
    int SourceHeight,
    string? ApplicationVersion);

public sealed record SessionRecordingSummary(
    int TimelineEntries,
    int ValidObservations,
    int UnavailableObservations,
    int SavedMapFrames,
    int SkippedCadenceCandidates,
    int ReplacedByHigherConfidence,
    int GapCount,
    TimeSpan? FirstAcceptedGameTime,
    TimeSpan? LastAcceptedGameTime,
    TimeSpan? AchievedGameTimeResolution,
    bool StartsUnavailable,
    bool EndsUnavailable,
    string? Warning);

public sealed record MapObservationCandidate(
    TimelineObservation Observation,
    MapImage Image)
{
    public MapObservationCandidate Validate()
    {
        ArgumentNullException.ThrowIfNull(Observation);
        ArgumentNullException.ThrowIfNull(Image);
        Image.Validate();
        if (Observation.Status != ObservationStatus.Valid ||
            Observation.GameTime is null ||
            Observation.SourceFrameSequence != Image.SourceFrameSequence ||
            Observation.SourceTimestamp != Image.SourceTimestamp)
        {
            throw new ArgumentException("Map candidate must contain a same-frame valid observation and image.");
        }

        return this;
    }
}
