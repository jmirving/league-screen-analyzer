using LeagueScreenAnalyzer.Capture.Processing;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ObservationPolicyCadenceGapTests
{
    [Fact]
    public void Policy_AcceptsOnlySameFrameTrustedEvidence()
    {
        ObservationPolicy policy = new();
        SourceFrame source = Source(7);
        RegionFrame mapFrame = Region(7);

        TimelineObservation accepted = policy.Create(
            source,
            mapFrame,
            Clock(7, TimeSpan.FromMinutes(18)),
            Map(7, 0.8),
            SessionMode.ReplayContinuous);
        TimelineObservation mismatched = policy.Create(
            source,
            mapFrame,
            Clock(6, TimeSpan.FromMinutes(18)),
            Map(7, 0.8),
            SessionMode.ReplayContinuous);

        Assert.Equal(ObservationStatus.Valid, accepted.Status);
        Assert.Equal(ObservationStatus.Unavailable, mismatched.Status);
        Assert.Equal("source-frame-mismatch", mismatched.UnavailabilityReason);
    }

    [Theory]
    [InlineData(false, true, "clock-unavailable")]
    [InlineData(true, false, "minimap-unavailable")]
    [InlineData(false, false, "clock-unavailable")]
    public void Policy_MarksInvalidEvidenceUnavailable(
        bool clockValid,
        bool mapValid,
        string reason)
    {
        ObservationPolicy policy = new();
        ClockReading clock = clockValid
            ? Clock(1, TimeSpan.FromSeconds(59))
            : new ClockReading(
                null,
                0,
                ClockReadingStatus.NotVisible,
                sourceFrameSequence: 1,
                sourceTimestamp: TimeSpan.FromMilliseconds(100));
        MapValidationResult map = mapValid
            ? Map(1, 0.8)
            : new MapValidationResult(
                MapFrameStatus.Obscured,
                0,
                ["hidden"],
                "test",
                sourceFrameSequence: 1,
                sourceTimestamp: TimeSpan.FromMilliseconds(100));

        TimelineObservation result =
            policy.Create(Source(1), Region(1), clock, map, SessionMode.ReplayContinuous);

        Assert.Equal(ObservationStatus.Unavailable, result.Status);
        Assert.Equal(reason, result.UnavailabilityReason);
        Assert.Null(result.GameTime);
    }

    [Fact]
    public void Cadence_KeepsOneHighestConfidenceCandidatePerBucket()
    {
        ObservationCadence cadence = new(TimeSpan.FromSeconds(1));
        MapObservationCandidate first = Candidate(1, 10_100, 0.6);
        MapObservationCandidate better = Candidate(2, 10_800, 0.9);
        MapObservationCandidate next = Candidate(3, 11_000, 0.7);

        Assert.Null(cadence.Offer(first));
        Assert.Null(cadence.Offer(better));
        MapObservationCandidate emitted = Assert.IsType<MapObservationCandidate>(cadence.Offer(next));

        Assert.Equal(2, emitted.Observation.SourceFrameSequence);
        Assert.Equal(1, cadence.HigherConfidenceReplacements);
        emitted.Image.Dispose();
        cadence.Complete()!.Image.Dispose();
    }

    [Fact]
    public void GapDetector_PreservesDistinctReasonsAndDoesNotInterpolate()
    {
        TimelineObservation[] observations =
        [
            Available(1, 10_000),
            Unavailable(2, "clock-unavailable"),
            Unavailable(3, "minimap-unavailable"),
            Available(4, 15_000)
        ];

        GapInterval gap = Assert.Single(GapDetector.Detect(observations));

        Assert.Equal(TimeSpan.FromSeconds(10), gap.StartGameTime);
        Assert.Equal(TimeSpan.FromSeconds(15), gap.EndGameTime);
        Assert.Equal("clock-unavailable+minimap-unavailable", gap.Reason);
    }

    [Fact]
    public void GapDetector_HandlesOpenSessionBoundariesWithoutFabricatingGaps()
    {
        TimelineObservation[] observations =
        [
            Unavailable(1, "clock-unavailable"),
            Available(2, 10_000),
            Unavailable(3, "minimap-unavailable")
        ];

        Assert.Empty(GapDetector.Detect(observations));
    }

    internal static TimelineObservation Available(long sequence, int milliseconds)
    {
        SourceFrame source = Source(sequence);
        return new ObservationPolicy().Create(
            source,
            Region(sequence),
            Clock(sequence, TimeSpan.FromMilliseconds(milliseconds)),
            Map(sequence, 0.8),
            SessionMode.ReplayContinuous);
    }

    internal static TimelineObservation Unavailable(long sequence, string reason)
    {
        SourceFrame source = Source(sequence);
        MapValidationResult map = reason == "minimap-unavailable"
            ? new MapValidationResult(
                MapFrameStatus.Obscured,
                0,
                ["hidden"],
                "test",
                sourceFrameSequence: sequence,
                sourceTimestamp: source.SourceTimestamp)
            : Map(sequence, 0.8);
        ClockReading clock = reason == "clock-unavailable"
            ? new ClockReading(
                null,
                0,
                ClockReadingStatus.NotVisible,
                sourceFrameSequence: sequence,
                sourceTimestamp: source.SourceTimestamp)
            : Clock(sequence, TimeSpan.FromSeconds(sequence));
        return new ObservationPolicy().Create(
            source,
            Region(sequence),
            clock,
            map,
            SessionMode.ReplayContinuous);
    }

    internal static ClockReading Clock(long sequence, TimeSpan gameTime) =>
        new(
            gameTime,
            0.9,
            ClockReadingStatus.Valid,
            temporalStatus: ClockTemporalStatus.Accepted,
            sourceFrameSequence: sequence,
            sourceTimestamp: TimeSpan.FromMilliseconds(sequence * 100));

    internal static MapValidationResult Map(long sequence, double confidence) =>
        new(
            MapFrameStatus.Valid,
            confidence,
            [],
            "test-minimap",
            new MapFeatureValues(128, 128, 1, 80, 1000, 0, 255, 0.1, 0.1, 0.2, 0.8, 0.8),
            sequence,
            TimeSpan.FromMilliseconds(sequence * 100));

    private static MapObservationCandidate Candidate(long sequence, int milliseconds, double confidence)
    {
        TimelineObservation observation = Available(sequence, milliseconds);
        MapValidationResult map = Map(sequence, confidence);
        observation = new TimelineObservation(
            observation.SourceTimestamp,
            observation.GameTime,
            observation.Status,
            observation.ClockResult,
            map,
            sourceFrameSequence: sequence);
        return new MapObservationCandidate(
            observation,
            MinimapFeatureAndValidationTests.Image(
                128,
                128,
                (x, y) => (byte)((x + y) % 255),
                sequence));
    }

    private static SourceFrame Source(long sequence) =>
        new(
            sequence,
            TimeSpan.FromMilliseconds(sequence * 100),
            1920,
            1080,
            Payload.Instance);

    private static RegionFrame Region(long sequence) =>
        new(
            RegionType.Minimap,
            sequence,
            TimeSpan.FromMilliseconds(sequence * 100),
            128,
            128,
            Payload.Instance);

    private sealed class Payload : IFramePayload
    {
        public static Payload Instance { get; } = new();
    }
}
