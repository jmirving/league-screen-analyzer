using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockTemporalValidatorTests
{
    [Fact]
    public void FirstValidReading_IsAccepted()
    {
        ClockReading reading = Validate(new ClockTemporalValidator(), Valid("0:00"), Profile(), 0);
        Assert.Equal(ClockReadingStatus.Valid, reading.Status);
        Assert.Equal(TimeSpan.Zero, reading.GameTime);
    }

    [Theory]
    [InlineData("0:00", 0.25)]
    [InlineData("0:01", 1)]
    [InlineData("10:00", 1)]
    public void RepeatedIncrementAndMinuteRollover_AreAccepted(string next, double elapsed)
    {
        ClockTemporalValidator validator = new();
        Validate(validator, Valid(next == "10:00" ? "9:59" : "0:00"), Profile(), 0);
        ClockReading reading = Validate(validator, Valid(next), Profile(), elapsed);
        Assert.Equal(ClockReadingStatus.Valid, reading.Status);
    }

    [Fact]
    public void BriefUnavailable_DoesNotFabricateOrLoseHistory()
    {
        ClockTemporalValidator validator = new();
        Validate(validator, Valid("12:43"), Profile(), 0);
        ClockReading unavailable = Validate(validator, Unavailable(), Profile(), 0.5);
        ClockReading recovered = Validate(validator, Valid("12:44"), Profile(), 1);

        Assert.Null(unavailable.GameTime);
        Assert.Equal(ClockReadingStatus.Unreadable, unavailable.Status);
        Assert.Equal(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(43), unavailable.LastAcceptedGameTime);
        Assert.Equal(ClockReadingStatus.Valid, recovered.Status);
    }

    [Fact]
    public void BackwardCandidate_IsRejectedAndPreservesHistory()
    {
        ClockTemporalValidator validator = new();
        Validate(validator, Valid("12:43"), Profile(), 0);
        ClockReading reading = Validate(validator, Valid("12:42"), Profile(), 1);

        Assert.Equal(ClockReadingStatus.Backward, reading.Status);
        Assert.Null(reading.GameTime);
        Assert.Equal(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(43), reading.LastAcceptedGameTime);
    }

    [Fact]
    public void ImplausibleForwardJump_IsRejected()
    {
        ClockTemporalValidator validator = new();
        Validate(validator, Valid("1:00"), Profile(), 0);
        Assert.Equal(
            ClockReadingStatus.Implausible,
            Validate(validator, Valid("1:10"), Profile(), 1).Status);
    }

    [Fact]
    public void SourceTimestampRegression_IsDiscontinuous()
    {
        ClockTemporalValidator validator = new();
        Validate(validator, Valid("1:00"), Profile(), 2);
        Assert.Equal(
            ClockReadingStatus.Discontinuous,
            Validate(validator, Valid("1:01"), Profile(), 1).Status);
    }

    [Theory]
    [InlineData(0.25, 4, "0:01")]
    [InlineData(1, 1, "0:01")]
    [InlineData(4, 1, "0:04")]
    [InlineData(8, 1, "0:08")]
    public void PlaybackSpeed_ScalesExpectedAdvance(double speed, double elapsed, string next)
    {
        ClockTemporalValidator validator = new();
        ClockRecognitionProfile profile = Profile(speed);
        Validate(validator, Valid("0:00"), profile, 0);
        Assert.Equal(ClockReadingStatus.Valid, Validate(validator, Valid(next), profile, elapsed).Status);
    }

    [Fact]
    public void LongMissingInterval_IsDiscontinuousInReplayMode()
    {
        ClockTemporalValidator validator = new();
        Validate(validator, Valid("1:00"), Profile(), 0);
        Validate(validator, Unavailable(), Profile(), 1);
        Assert.Equal(
            ClockReadingStatus.Discontinuous,
            Validate(validator, Valid("1:04"), Profile(), 4).Status);
    }

    [Fact]
    public void UnreadableInput_NeverUsesExpectedTime()
    {
        ClockTemporalValidator validator = new();
        Validate(validator, Valid("1:00"), Profile(), 0);
        ClockReading reading = Validate(validator, Unavailable(), Profile(), 1);
        Assert.Null(reading.GameTime);
        Assert.Null(reading.RawRecognizedText);
        Assert.Equal(ClockTemporalStatus.Rejected, reading.TemporalStatus);
    }

    private static ClockRecognitionProfile Profile(double speed = 1) =>
        BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id).WithPlaybackSpeed(speed);

    private static ClockReading Validate(
        ClockTemporalValidator validator,
        ClockRecognitionResult result,
        ClockRecognitionProfile profile,
        double seconds) =>
        validator.Validate(result, profile, (long)(seconds * 10), TimeSpan.FromSeconds(seconds));

    private static ClockRecognitionResult Valid(string text)
    {
        Assert.True(ClockParser.TryParse(text, TimeSpan.FromHours(3), out TimeSpan parsed, out _));
        ClockCandidate candidate = new(
            text,
            parsed,
            0.99,
            text.Select(character => new ClockCharacterCandidate(character, 0.99)).ToArray());
        return new ClockRecognitionResult(
            [candidate],
            ClockReadingStatus.Valid,
            0.99,
            null,
            new ClockRecognitionDiagnostics(1, 1, [255], [], "test"));
    }

    private static ClockRecognitionResult Unavailable() =>
        new(
            [],
            ClockReadingStatus.Unreadable,
            0,
            "No supported image evidence.",
            new ClockRecognitionDiagnostics(1, 1, [0], [], "test"));
}
