using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Capture.Processing;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Storage;

public sealed class SessionDatasetRecorder : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonOptions)
    {
        WriteIndented = false
    };

    private readonly object _gate = new();
    private readonly SessionRecordingConfiguration _configuration;
    private readonly ObservationCadence _cadence;
    private readonly List<TimelineObservation> _timeline = [];
    private readonly Dictionary<long, string> _savedPaths = [];
    private readonly List<TimeSpan> _savedGameTimes = [];
    private bool _stopped;

    public SessionDatasetRecorder(
        string parentDirectory,
        SessionRecordingConfiguration configuration,
        string? applicationVersion = null,
        DateTimeOffset? startedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        _configuration = configuration.Validate();
        _cadence = new ObservationCadence(configuration.RequestedGameTimeCadence);
        DateTimeOffset start = startedAt ?? DateTimeOffset.Now;
        SessionId = $"{start:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..37];
        OutputDirectory = Path.Combine(Path.GetFullPath(parentDirectory), $"session-{SessionId}");
        Directory.CreateDirectory(Path.Combine(OutputDirectory, "map", "frames"));
        Directory.CreateDirectory(Path.Combine(OutputDirectory, "diagnostics", "invalid-map"));
        Directory.CreateDirectory(Path.Combine(OutputDirectory, "diagnostics", "invalid-clock"));
        ApplicationVersion = applicationVersion;
    }

    public string SessionId { get; }

    public string OutputDirectory { get; }

    public string? ApplicationVersion { get; }

    public int SavedMapFrameCount
    {
        get
        {
            lock (_gate)
            {
                return _savedPaths.Count;
            }
        }
    }

    public void Record(TimelineObservation observation, MapImage? acceptedImage = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            _timeline.Add(observation);
            if (observation.Status != ObservationStatus.Valid)
            {
                acceptedImage?.Dispose();
                return;
            }

            if (acceptedImage is null)
            {
                throw new ArgumentException("A valid observation requires its same-frame minimap image.");
            }

            MapObservationCandidate? ready = _cadence.Offer(
                new MapObservationCandidate(observation, acceptedImage).Validate());
            if (ready is not null)
            {
                SaveCandidate(ready);
            }
        }
    }

    public async Task<SessionRecordingSummary> StopAsync(
        CancellationToken cancellationToken = default)
    {
        TimelineObservation[] timeline;
        lock (_gate)
        {
            if (!_stopped)
            {
                MapObservationCandidate? final = _cadence.Complete();
                if (final is not null)
                {
                    SaveCandidate(final);
                }

                _stopped = true;
            }

            timeline = _timeline.ToArray();
        }

        IReadOnlyList<GapInterval> gaps = GapDetector.Detect(timeline);
        TimelineObservation[] enriched = timeline.Select(observation =>
        {
            string? path = observation.SourceFrameSequence is long sequence &&
                           _savedPaths.TryGetValue(sequence, out string? savedPath)
                ? savedPath
                : observation.MapArtifactPath;
            return new TimelineObservation(
                observation.SourceTimestamp,
                observation.GameTime,
                observation.Status,
                observation.ClockResult,
                observation.MapResult,
                path,
                observation.SourceFrameSequence,
                observation.UnavailabilityReason);
        }).ToArray();
        TimelineObservation[] valid =
            enriched.Where(value => value.Status == ObservationStatus.Valid).ToArray();
        bool startsUnavailable = enriched.FirstOrDefault()?.Status == ObservationStatus.Unavailable;
        bool endsUnavailable = enriched.LastOrDefault()?.Status == ObservationStatus.Unavailable;
        TimeSpan? achieved = CalculateAchievedResolution(_savedGameTimes);
        string? warning = _configuration.Mode == SessionMode.ReplayContinuous &&
                          gaps.Any(gap => gap.EndGameTime - gap.StartGameTime >= TimeSpan.FromSeconds(5))
            ? "ReplayContinuous contains a long unavailable interval."
            : null;
        SessionRecordingSummary summary = new(
            enriched.Length,
            valid.Length,
            enriched.Length - valid.Length,
            _savedPaths.Count,
            _cadence.SkippedCandidates,
            _cadence.HigherConfidenceReplacements,
            gaps.Count,
            valid.FirstOrDefault()?.GameTime,
            valid.LastOrDefault()?.GameTime,
            achieved,
            startsUnavailable,
            endsUnavailable,
            warning);
        SessionManifest manifest = new(
            "1.0",
            SessionId,
            _configuration.SourceType,
            _configuration.Mode,
            _configuration.CaptureLayout,
            _configuration.ClockProfileId,
            _configuration.MinimapProfileId,
            _configuration.PlaybackSpeed,
            _configuration.RequestedGameTimeCadence,
            enriched.FirstOrDefault()?.SourceTimestamp,
            enriched.LastOrDefault()?.SourceTimestamp,
            summary.FirstAcceptedGameTime,
            summary.LastAcceptedGameTime,
            _configuration.SourceWidth,
            _configuration.SourceHeight,
            ApplicationVersion);

        await WriteJsonAtomicAsync("manifest.json", manifest, cancellationToken).ConfigureAwait(false);
        await WriteTimelineAtomicAsync(enriched, cancellationToken).ConfigureAwait(false);
        await WriteJsonAtomicAsync("summary.json", summary, cancellationToken).ConfigureAwait(false);
        await WriteJsonAtomicAsync("gaps.json", gaps, cancellationToken).ConfigureAwait(false);
        return summary;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void SaveCandidate(MapObservationCandidate candidate)
    {
        try
        {
            long milliseconds = candidate.Observation.GameTime!.Value.Ticks / TimeSpan.TicksPerMillisecond;
            string fileName = $"{milliseconds:D9}.bmp";
            string relativePath = Path.Combine("map", "frames", fileName).Replace('\\', '/');
            string fullPath = Path.Combine(OutputDirectory, "map", "frames", fileName);
            WriteLosslessBgraBmp(fullPath, candidate.Image);
            _savedPaths[candidate.Image.SourceFrameSequence] = relativePath;
            _savedGameTimes.Add(candidate.Observation.GameTime.Value);
        }
        finally
        {
            candidate.Image.Dispose();
        }
    }

    private async Task WriteTimelineAtomicAsync(
        IEnumerable<TimelineObservation> observations,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(OutputDirectory, "timeline.jsonl");
        string temporaryPath = path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        await using (StreamWriter writer = new(stream))
        {
            foreach (TimelineObservation observation in observations)
            {
                string line = JsonSerializer.Serialize(observation, JsonLineOptions);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(temporaryPath, path, true);
    }

    private async Task WriteJsonAtomicAsync<T>(
        string fileName,
        T value,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(OutputDirectory, fileName);
        string temporaryPath = path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, true);
    }

    private static TimeSpan? CalculateAchievedResolution(IReadOnlyList<TimeSpan> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        long[] differences = values
            .Zip(values.Skip(1), (left, right) => (right - left).Ticks)
            .Where(value => value > 0)
            .Order()
            .ToArray();
        return differences.Length == 0 ? null : TimeSpan.FromTicks(differences[differences.Length / 2]);
    }

    private static void WriteLosslessBgraBmp(string path, MapImage image)
    {
        image.Validate();
        int rowSize = checked(image.Width * 4);
        int pixelBytes = checked(rowSize * image.Height);
        Span<byte> header = stackalloc byte[54];
        header.Clear();
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], 54 + pixelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], 54);
        BinaryPrimitives.WriteInt32LittleEndian(header[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], -image.Height);
        BinaryPrimitives.WriteInt16LittleEndian(header[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[28..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[34..], pixelBytes);
        using FileStream stream = File.Create(path);
        stream.Write(header);
        ReadOnlySpan<byte> pixels = image.BgraPixels.Span;
        for (int row = 0; row < image.Height; row++)
        {
            stream.Write(pixels.Slice(row * image.Stride, rowSize));
        }
    }
}
