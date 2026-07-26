using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public sealed class GameClockReader(
    IClockImageRecognizer recognizer,
    IClockTemporalValidator temporalValidator,
    ClockRecognitionProfile profile) : IGameClockReader
{
    private readonly IClockImageRecognizer _recognizer =
        recognizer ?? throw new ArgumentNullException(nameof(recognizer));
    private readonly IClockTemporalValidator _temporalValidator =
        temporalValidator ?? throw new ArgumentNullException(nameof(temporalValidator));
    private ClockRecognitionProfile _profile =
        (profile ?? throw new ArgumentNullException(nameof(profile))).Validate();

    public ClockRecognitionProfile Profile => _profile;

    public void SetPlaybackSpeed(double playbackSpeed)
    {
        if (!double.IsFinite(playbackSpeed) || playbackSpeed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackSpeed));
        }

        _profile = _profile.WithPlaybackSpeed(playbackSpeed).Validate();
    }

    public void Reset() => _temporalValidator.Reset();

    public async ValueTask<ClockReading> ReadAsync(
        RegionFrame clockFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clockFrame);
        if (clockFrame.RegionType != RegionType.Clock)
        {
            throw new ArgumentException("The supplied region is not a CLOCK region.", nameof(clockFrame));
        }

        if (clockFrame.Payload is not IClockImagePayload payload)
        {
            throw new InvalidOperationException(
                $"Clock payload must implement {nameof(IClockImagePayload)}.");
        }

        ClockImage image = new(
            clockFrame.Width,
            clockFrame.Height,
            payload.Stride,
            payload.BgraPixels,
            clockFrame.SourceFrameSequence,
            clockFrame.SourceTimestamp);
        ClockRecognitionResult recognition =
            await _recognizer.RecognizeAsync(image, _profile, cancellationToken).ConfigureAwait(false);
        return _temporalValidator.Validate(
            recognition,
            _profile,
            clockFrame.SourceFrameSequence,
            clockFrame.SourceTimestamp);
    }
}
