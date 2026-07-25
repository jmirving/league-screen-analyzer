using System.Runtime.CompilerServices;
using System.Text.Json;
using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Fixtures;

public sealed class FixtureFrameSource : IFrameSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _manifestPath;

    public FixtureFrameSource(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        _manifestPath = Path.GetFullPath(manifestPath);
    }

    public async IAsyncEnumerable<SourceFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        FixtureManifest manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        long? previousSequence = null;
        long? previousSourceTimeMs = null;

        foreach (FixtureFrameDefinition frame in manifest.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateFrame(frame, previousSequence, previousSourceTimeMs);

            yield return new SourceFrame(
                frame.Sequence,
                TimeSpan.FromMilliseconds(frame.SourceTimeMs),
                frame.Width,
                frame.Height,
                new FixtureFramePayload(frame.ClockText, frame.ClockVisible, frame.MapVisible));

            previousSequence = frame.Sequence;
            previousSourceTimeMs = frame.SourceTimeMs;
        }
    }

    public async Task<FixtureManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_manifestPath))
        {
            throw new FileNotFoundException($"Fixture manifest was not found: '{_manifestPath}'.", _manifestPath);
        }

        await using FileStream stream = File.OpenRead(_manifestPath);
        FixtureManifest? manifest;

        try
        {
            manifest = await JsonSerializer.DeserializeAsync<FixtureManifest>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Fixture manifest '{_manifestPath}' contains invalid JSON.", exception);
        }

        if (manifest is null)
        {
            throw new InvalidDataException($"Fixture manifest '{_manifestPath}' is empty.");
        }

        if (manifest.Frames.Count == 0)
        {
            throw new InvalidDataException($"Fixture manifest '{_manifestPath}' must contain at least one frame.");
        }

        return manifest;
    }

    private static void ValidateFrame(
        FixtureFrameDefinition frame,
        long? previousSequence,
        long? previousSourceTimeMs)
    {
        if (frame.Sequence < 0)
        {
            throw new InvalidDataException("Fixture frame sequence numbers cannot be negative.");
        }

        if (previousSequence is not null && frame.Sequence <= previousSequence)
        {
            throw new InvalidDataException("Fixture frame sequence numbers must increase strictly.");
        }

        if (frame.SourceTimeMs < 0)
        {
            throw new InvalidDataException("Fixture source timestamps cannot be negative.");
        }

        if (previousSourceTimeMs is not null && frame.SourceTimeMs < previousSourceTimeMs)
        {
            throw new InvalidDataException("Fixture source timestamps cannot move backward.");
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new InvalidDataException("Fixture frame dimensions must be greater than zero.");
        }
    }
}
