using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Storage;

public sealed class JsonSessionArtifactWriter : ISessionArtifactWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions SummarySerializerOptions = new(SerializerOptions)
    {
        WriteIndented = true
    };

    private readonly string _outputDirectory;

    public JsonSessionArtifactWriter(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _outputDirectory = Path.GetFullPath(outputDirectory);
    }

    public async Task WriteAsync(
        SessionProcessingResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        Directory.CreateDirectory(_outputDirectory);

        string timelinePath = Path.Combine(_outputDirectory, "timeline.jsonl");
        await using (FileStream timelineStream = File.Create(timelinePath))
        await using (StreamWriter timelineWriter = new(timelineStream))
        {
            foreach (TimelineObservation observation in result.Observations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string json = JsonSerializer.Serialize(observation, SerializerOptions);
                await timelineWriter.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        string summaryPath = Path.Combine(_outputDirectory, "summary.json");
        await using FileStream summaryStream = File.Create(summaryPath);
        await JsonSerializer.SerializeAsync(
            summaryStream,
            result.Summary,
            SummarySerializerOptions,
            cancellationToken).ConfigureAwait(false);

        string gapsPath = Path.Combine(_outputDirectory, "gaps.json");
        await using FileStream gapsStream = File.Create(gapsPath);
        await JsonSerializer.SerializeAsync(
            gapsStream,
            result.Gaps,
            SummarySerializerOptions,
            cancellationToken).ConfigureAwait(false);
    }
}
