using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public sealed class ClockDiagnosticWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Write(
        string rootDirectory,
        ClockRecognitionObservation observation,
        ClockRecognitionProfile profile,
        ClockSampleLabel? explicitLabel,
        bool isUnlabeledDiagnostic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(profile);
        if ((explicitLabel is null) == !isUnlabeledDiagnostic)
        {
            throw new ArgumentException(
                "Provide a parsed explicit label for a labeled sample, or explicitly mark the sample as an unlabeled diagnostic.");
        }

        string directory = Path.Combine(
            Path.GetFullPath(rootDirectory),
            $"clock-sample-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{observation.Image.SourceFrameSequence}");
        Directory.CreateDirectory(directory);
        WriteBgraBitmap(Path.Combine(directory, "original-clock.bmp"), observation.Image);
        WritePgm(
            Path.Combine(directory, "normalized-clock.pgm"),
            observation.Recognition.Diagnostics.NormalizedPixels,
            observation.Recognition.Diagnostics.NormalizedWidth,
            observation.Recognition.Diagnostics.NormalizedHeight);

        for (int i = 0; i < observation.Recognition.Diagnostics.Segments.Count; i++)
        {
            ClockSegment segment = observation.Recognition.Diagnostics.Segments[i];
            WritePgm(
                Path.Combine(directory, $"segment-{i:00}.pgm"),
                segment.Pixels,
                segment.Width,
                segment.Height);
        }

        object result = new
        {
            schemaVersion = 2,
            sampleKind = isUnlabeledDiagnostic ? "unlabeledDiagnostic" : "labeled",
            profile = new { profile.Id, profile.Name, profile.Version },
            profile.PlaybackSpeed,
            observation.Image.SourceFrameSequence,
            observation.Image.SourceTimestamp,
            explicitLabel = explicitLabel?.Value,
            explicitLabelSeconds = explicitLabel?.TotalSeconds,
            explicitLabelMilliseconds = explicitLabel?.TotalMilliseconds,
            observation.ActualSamplesPerSecond,
            observation.Recognition,
            observation.Reading,
            temporalHistory = new
            {
                observation.Reading.LastAcceptedGameTime,
                observation.Reading.LastAcceptedSourceTimestamp
            }
        };
        File.WriteAllText(
            Path.Combine(directory, "result.json"),
            JsonSerializer.Serialize(result, JsonOptions));
        return directory;
    }

    private static void WritePgm(string path, byte[] pixels, int width, int height)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes($"P5\n{width} {height}\n255\n"));
        writer.Write(pixels);
    }

    private static void WriteBgraBitmap(string path, ClockImage image)
    {
        int outputStride = image.Width * 4;
        int pixelBytes = outputStride * image.Height;
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write((ushort)0x4D42);
        writer.Write(54 + pixelBytes);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(image.Width);
        writer.Write(image.Height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        ReadOnlySpan<byte> pixels = image.BgraPixels.Span;
        for (int y = image.Height - 1; y >= 0; y--)
        {
            writer.Write(pixels.Slice(y * image.Stride, outputStride));
        }
    }
}
