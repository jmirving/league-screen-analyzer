using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public sealed class ClockTemporalValidator : IClockTemporalValidator
{
    private TimeSpan? _lastAcceptedGameTime;
    private TimeSpan? _lastAcceptedSourceTimestamp;
    private TimeSpan? _lastObservationSourceTimestamp;
    private TimeSpan? _unavailableSinceSourceTimestamp;

    public ClockTemporalContext Context => new(
        _lastAcceptedGameTime,
        _lastAcceptedSourceTimestamp,
        _lastObservationSourceTimestamp,
        _unavailableSinceSourceTimestamp);

    public ClockReading Validate(
        ClockRecognitionResult recognition,
        ClockRecognitionProfile profile,
        long sourceFrameSequence,
        TimeSpan sourceTimestamp)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        profile.Validate();

        if (_lastObservationSourceTimestamp is TimeSpan previousObservation &&
            sourceTimestamp < previousObservation)
        {
            return Reject(
                ClockReadingStatus.Discontinuous,
                recognition,
                sourceFrameSequence,
                sourceTimestamp,
                $"Source timestamp regressed from {previousObservation} to {sourceTimestamp}.");
        }

        _lastObservationSourceTimestamp = sourceTimestamp;

        ClockCandidate? candidate = recognition.BestCandidate;
        if (recognition.Status != ClockReadingStatus.Valid ||
            candidate?.ParsedGameTime is not TimeSpan candidateTime)
        {
            _unavailableSinceSourceTimestamp ??= sourceTimestamp;
            return Reject(
                recognition.Status == ClockReadingStatus.Valid
                    ? ClockReadingStatus.Malformed
                    : recognition.Status,
                recognition,
                sourceFrameSequence,
                sourceTimestamp,
                recognition.DiagnosticReason ?? candidate?.Diagnostic ?? "No accepted image candidate.");
        }

        if (candidate.Confidence < profile.MinimumRecognitionConfidence)
        {
            _unavailableSinceSourceTimestamp ??= sourceTimestamp;
            return Reject(
                ClockReadingStatus.LowConfidence,
                recognition,
                sourceFrameSequence,
                sourceTimestamp,
                "Image candidate is below the profile confidence threshold.");
        }

        if (_lastAcceptedGameTime is TimeSpan lastGame &&
            _lastAcceptedSourceTimestamp is TimeSpan lastSource)
        {
            if (_unavailableSinceSourceTimestamp is TimeSpan unavailableSince &&
                profile.ValidationMode == ClockValidationMode.ReplayContinuous &&
                sourceTimestamp - unavailableSince >= profile.LongMissingInterval)
            {
                return Reject(
                    ClockReadingStatus.Discontinuous,
                    recognition,
                    sourceFrameSequence,
                    sourceTimestamp,
                    "A long unavailable interval broke replay-continuous validation.");
            }

            if (candidateTime + profile.BackwardTolerance < lastGame)
            {
                return Reject(
                    ClockReadingStatus.Backward,
                    recognition,
                    sourceFrameSequence,
                    sourceTimestamp,
                    $"Candidate moved backward from {Format(lastGame)} to {Format(candidateTime)}.");
            }

            TimeSpan sourceElapsed = sourceTimestamp - lastSource;
            double expectedSeconds = sourceElapsed.TotalSeconds * profile.PlaybackSpeed;
            TimeSpan maximumAdvance = TimeSpan.FromSeconds(Math.Max(0, expectedSeconds))
                                      + profile.ForwardTolerance;
            if (candidateTime - lastGame > maximumAdvance)
            {
                return Reject(
                    profile.ValidationMode == ClockValidationMode.ReplayContinuous
                        ? ClockReadingStatus.Implausible
                        : ClockReadingStatus.Discontinuous,
                    recognition,
                    sourceFrameSequence,
                    sourceTimestamp,
                    $"Candidate advanced {candidateTime - lastGame:g}; expected at most {maximumAdvance:g} at {profile.PlaybackSpeed:0.##}x.");
            }
        }

        _lastAcceptedGameTime = candidateTime;
        _lastAcceptedSourceTimestamp = sourceTimestamp;
        _unavailableSinceSourceTimestamp = null;
        return new ClockReading(
            candidateTime,
            candidate.Confidence,
            ClockReadingStatus.Valid,
            rawRecognizedText: candidate.Text,
            bestCandidate: candidate,
            imageRecognitionConfidence: recognition.Confidence,
            temporalStatus: ClockTemporalStatus.Accepted,
            sourceFrameSequence: sourceFrameSequence,
            sourceTimestamp: sourceTimestamp);
    }

    public void Reset()
    {
        _lastAcceptedGameTime = null;
        _lastAcceptedSourceTimestamp = null;
        _lastObservationSourceTimestamp = null;
        _unavailableSinceSourceTimestamp = null;
    }

    private ClockReading Reject(
        ClockReadingStatus status,
        ClockRecognitionResult recognition,
        long sourceFrameSequence,
        TimeSpan sourceTimestamp,
        string reason)
    {
        ClockCandidate? candidate = recognition.BestCandidate;
        return new ClockReading(
            null,
            candidate?.Confidence ?? recognition.Confidence,
            status,
            reason,
            candidate?.Text,
            candidate,
            recognition.Confidence,
            ClockTemporalStatus.Rejected,
            sourceFrameSequence,
            sourceTimestamp,
            _lastAcceptedGameTime,
            _lastAcceptedSourceTimestamp);
    }

    private static string Format(TimeSpan value) =>
        $"{(int)value.TotalMinutes}:{value.Seconds:00}";
}
