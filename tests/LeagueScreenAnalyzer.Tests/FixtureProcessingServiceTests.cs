using LeagueScreenAnalyzer.Cli;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class FixtureProcessingServiceTests
{
    [Fact]
    public async Task ProcessAsync_WritesTimelineAndSummaryWithoutChildProcess()
    {
        using TemporaryDirectory directory = new();
        string manifestPath = Path.Combine(directory.Path, "session.json");
        string outputPath = Path.Combine(directory.Path, "artifacts");
        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "frames": [
                {
                  "sequence": 1,
                  "sourceTimeMs": 0,
                  "width": 1920,
                  "height": 1080,
                  "clockText": "12:43",
                  "clockVisible": true,
                  "mapVisible": true
                }
              ]
            }
            """);

        SessionProcessingResult result =
            await new FixtureProcessingService().ProcessAsync(manifestPath, outputPath);

        Assert.Equal(1, result.Summary.TotalFrames);
        Assert.Equal(1, result.Summary.ValidObservations);
        Assert.True(File.Exists(Path.Combine(outputPath, "timeline.jsonl")));
        Assert.True(File.Exists(Path.Combine(outputPath, "summary.json")));
    }
}
