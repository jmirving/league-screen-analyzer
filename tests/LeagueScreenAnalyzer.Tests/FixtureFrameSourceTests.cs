using LeagueScreenAnalyzer.Capture.Fixtures;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class FixtureFrameSourceTests
{
    [Fact]
    public async Task ReadFramesAsync_ParsesManifestMetadata()
    {
        using TemporaryDirectory directory = new();
        string manifestPath = Path.Combine(directory.Path, "session.json");
        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "frames": [
                {
                  "sequence": 7,
                  "sourceTimeMs": 250,
                  "width": 1280,
                  "height": 720,
                  "clockText": "03:14",
                  "clockVisible": true,
                  "mapVisible": false
                }
              ]
            }
            """);

        FixtureFrameSource source = new(manifestPath);
        List<SourceFrame> frames = [];
        await foreach (SourceFrame frame in source.ReadFramesAsync())
        {
            frames.Add(frame);
        }

        SourceFrame parsed = Assert.Single(frames);
        FixtureFramePayload payload = Assert.IsType<FixtureFramePayload>(parsed.Payload);
        Assert.Equal(7, parsed.SequenceNumber);
        Assert.Equal(TimeSpan.FromMilliseconds(250), parsed.SourceTimestamp);
        Assert.Equal("03:14", payload.ClockText);
        Assert.False(payload.MapVisible);
    }
}
