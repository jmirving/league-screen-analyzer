using System.Text.Json;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Storage;

namespace LeagueScreenAnalyzer.Tests;

public sealed class SessionDatasetRecorderTests
{
    [Fact]
    public async Task Recorder_WritesPortableDatasetAndCadenceSelectedLosslessFrames()
    {
        using TemporaryDirectory directory = new();
        SessionDatasetRecorder recorder = new(
            directory.Path,
            new SessionRecordingConfiguration(
                SessionMode.ReplayContinuous,
                TimeSpan.FromSeconds(1),
                "test-layout",
                "clock-v1",
                "map-v1",
                1,
                1920,
                1080),
            "1.2.3",
            DateTimeOffset.Parse("2026-07-26T12:00:00-04:00"));

        TimelineObservation first = ObservationPolicyCadenceGapTests.Available(1, 10_100);
        TimelineObservation duplicateBucket = ObservationPolicyCadenceGapTests.Available(2, 10_900);
        TimelineObservation unavailable =
            ObservationPolicyCadenceGapTests.Unavailable(3, "minimap-unavailable");
        TimelineObservation later = ObservationPolicyCadenceGapTests.Available(4, 12_000);
        recorder.Record(first, Image(1));
        recorder.Record(duplicateBucket, Image(2));
        recorder.Record(unavailable);
        recorder.Record(later, Image(4));

        SessionRecordingSummary summary = await recorder.StopAsync();

        Assert.Equal(4, summary.TimelineEntries);
        Assert.Equal(2, summary.SavedMapFrames);
        Assert.Equal(1, summary.GapCount);
        Assert.True(File.Exists(Path.Combine(recorder.OutputDirectory, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(recorder.OutputDirectory, "timeline.jsonl")));
        Assert.True(File.Exists(Path.Combine(recorder.OutputDirectory, "summary.json")));
        Assert.True(File.Exists(Path.Combine(recorder.OutputDirectory, "gaps.json")));
        Assert.Equal(2, Directory.EnumerateFiles(
            Path.Combine(recorder.OutputDirectory, "map", "frames"),
            "*.bmp").Count());
        string[] timeline = await File.ReadAllLinesAsync(
            Path.Combine(recorder.OutputDirectory, "timeline.jsonl"));
        Assert.Equal(4, timeline.Length);
        Assert.Contains(timeline, line => line.Contains("\"mapArtifactPath\":\"map/frames/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recorder_StopIsRepeatableAndManifestIsReadable()
    {
        using TemporaryDirectory directory = new();
        await using SessionDatasetRecorder recorder = new(
            directory.Path,
            new SessionRecordingConfiguration(
                SessionMode.BroadcastVod,
                TimeSpan.FromMilliseconds(500),
                "layout",
                "clock",
                "map",
                4,
                1280,
                720));
        recorder.Record(ObservationPolicyCadenceGapTests.Unavailable(1, "clock-unavailable"));

        SessionRecordingSummary first = await recorder.StopAsync();
        SessionRecordingSummary second = await recorder.StopAsync();
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(recorder.OutputDirectory, "manifest.json")));

        Assert.Equal(first, second);
        Assert.Equal("broadcastVod", manifest.RootElement.GetProperty("sessionMode").GetString());
        Assert.True(first.StartsUnavailable);
    }

    private static MapImage Image(long sequence) =>
        MinimapFeatureAndValidationTests.Image(
            128,
            128,
            (x, y) => (byte)((x * 3 + y * 5) % 255),
            sequence);
}
