using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Processing;

public sealed class ObservationCadence(TimeSpan cadence)
{
    private readonly TimeSpan _cadence = cadence > TimeSpan.Zero
        ? cadence
        : throw new ArgumentOutOfRangeException(nameof(cadence));
    private long? _currentBucket;
    private MapObservationCandidate? _best;

    public int SkippedCandidates { get; private set; }

    public int HigherConfidenceReplacements { get; private set; }

    public MapObservationCandidate? Offer(MapObservationCandidate candidate)
    {
        candidate.Validate();
        long bucket = candidate.Observation.GameTime!.Value.Ticks / _cadence.Ticks;
        if (_currentBucket is null)
        {
            _currentBucket = bucket;
            _best = candidate;
            return null;
        }

        if (bucket < _currentBucket)
        {
            candidate.Image.Dispose();
            SkippedCandidates++;
            return null;
        }

        if (bucket == _currentBucket)
        {
            if (candidate.Observation.MapResult.Confidence >
                _best!.Observation.MapResult.Confidence)
            {
                _best.Image.Dispose();
                _best = candidate;
                HigherConfidenceReplacements++;
            }
            else
            {
                candidate.Image.Dispose();
                SkippedCandidates++;
            }

            return null;
        }

        MapObservationCandidate completed = _best!;
        _currentBucket = bucket;
        _best = candidate;
        return completed;
    }

    public MapObservationCandidate? Complete()
    {
        MapObservationCandidate? completed = _best;
        _best = null;
        _currentBucket = null;
        return completed;
    }
}

public static class GapDetector
{
    public static IReadOnlyList<GapInterval> Detect(IReadOnlyList<TimelineObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        List<GapInterval> gaps = [];
        TimelineObservation? lastValid = null;
        List<string> unavailableReasons = [];

        foreach (TimelineObservation observation in observations)
        {
            if (observation.Status == ObservationStatus.Unavailable)
            {
                if (lastValid is not null)
                {
                    string reason = observation.UnavailabilityReason ?? "clock-or-minimap-unavailable";
                    if (unavailableReasons.Count == 0 ||
                        !string.Equals(unavailableReasons[^1], reason, StringComparison.Ordinal))
                    {
                        unavailableReasons.Add(reason);
                    }
                }

                continue;
            }

            if (lastValid?.GameTime is TimeSpan start &&
                observation.GameTime is TimeSpan end &&
                end > start &&
                unavailableReasons.Count > 0)
            {
                gaps.Add(new GapInterval(start, end, string.Join("+", unavailableReasons)));
            }

            lastValid = observation;
            unavailableReasons.Clear();
        }

        return gaps;
    }
}
