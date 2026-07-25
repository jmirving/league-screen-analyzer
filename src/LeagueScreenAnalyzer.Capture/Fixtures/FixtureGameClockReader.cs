using System.Globalization;
using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Fixtures;

public sealed class FixtureGameClockReader : IGameClockReader
{
    private readonly double _maximumPlaybackRate;
    private readonly TimeSpan _jumpTolerance;
    private TimeSpan? _lastGameTime;
    private TimeSpan? _lastSourceTimestamp;

    public FixtureGameClockReader(
        double maximumPlaybackRate = 4,
        TimeSpan? jumpTolerance = null)
    {
        if (!double.IsFinite(maximumPlaybackRate) || maximumPlaybackRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPlaybackRate),
                maximumPlaybackRate,
                "Maximum playback rate must be finite and greater than zero.");
        }

        _maximumPlaybackRate = maximumPlaybackRate;
        _jumpTolerance = jumpTolerance ?? TimeSpan.FromSeconds(2);
    }

    public ValueTask<ClockReading> ReadAsync(
        RegionFrame clockFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clockFrame);
        cancellationToken.ThrowIfCancellationRequested();

        if (clockFrame.RegionType != RegionType.Clock)
        {
            throw new ArgumentException("The supplied region is not a clock region.", nameof(clockFrame));
        }

        if (clockFrame.Payload is not FixtureFramePayload payload)
        {
            throw new InvalidOperationException(
                $"FixtureGameClockReader requires a {nameof(FixtureFramePayload)} payload.");
        }

        if (!payload.ClockVisible)
        {
            return ValueTask.FromResult(new ClockReading(
                null,
                0,
                ClockReadingStatus.NotVisible,
                "Clock region is not visible."));
        }

        if (!TryParseClock(payload.ClockText, out TimeSpan gameTime))
        {
            return ValueTask.FromResult(new ClockReading(
                null,
                0,
                ClockReadingStatus.Unparseable,
                $"Clock text '{payload.ClockText ?? "<null>"}' is not in m:ss format."));
        }

        if (_lastGameTime is not null && _lastSourceTimestamp is not null)
        {
            if (gameTime < _lastGameTime)
            {
                return ValueTask.FromResult(new ClockReading(
                    null,
                    0,
                    ClockReadingStatus.RejectedJump,
                    $"Clock moved backward from {Format(_lastGameTime.Value)} to {Format(gameTime)}."));
            }

            TimeSpan sourceDelta = clockFrame.SourceTimestamp - _lastSourceTimestamp.Value;
            TimeSpan maximumAdvance = TimeSpan.FromTicks(
                (long)(sourceDelta.Ticks * _maximumPlaybackRate)) + _jumpTolerance;

            if (gameTime - _lastGameTime > maximumAdvance)
            {
                return ValueTask.FromResult(new ClockReading(
                    null,
                    0,
                    ClockReadingStatus.RejectedJump,
                    $"Clock advanced implausibly from {Format(_lastGameTime.Value)} to {Format(gameTime)}."));
            }
        }

        _lastGameTime = gameTime;
        _lastSourceTimestamp = clockFrame.SourceTimestamp;
        return ValueTask.FromResult(new ClockReading(gameTime, 1, ClockReadingStatus.Valid));
    }

    private static bool TryParseClock(string? text, out TimeSpan gameTime)
    {
        gameTime = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) ||
            minutes < 0 ||
            seconds is < 0 or > 59 ||
            parts[1].Length != 2)
        {
            return false;
        }

        gameTime = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string Format(TimeSpan value) =>
        $"{(int)value.TotalMinutes}:{value.Seconds:00}";
}
