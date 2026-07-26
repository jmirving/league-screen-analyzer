using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public sealed class MinimapDiagnosticWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<string> WriteAsync(
        string parentDirectory,
        MapImage image,
        MinimapValidationProfile profile,
        MapValidationResult result,
        MinimapSampleLabel label,
        TimeSpan? acceptedGameTime,
        string? captureLayout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        image.Validate();
        profile.Validate();
        ArgumentNullException.ThrowIfNull(result);
        if (label == MinimapSampleLabel.Unlabeled && acceptedGameTime is not null)
        {
            // Clock metadata is evidence context only; retaining it is safe even for
            // unlabeled samples, but the sample's map label remains explicitly unlabeled.
        }

        string directory = Path.Combine(
            Path.GetFullPath(parentDirectory),
            $"minimap-sample-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{image.SourceFrameSequence}");
        Directory.CreateDirectory(directory);
        string originalPath = Path.Combine(directory, "minimap-original.bmp");
        string processedPath = Path.Combine(directory, "minimap-normalized.pgm");
        WriteBmp(originalPath, image);
        byte[] normalized = MinimapFeatureExtractor.NormalizeGrayscale(
            image,
            profile.NormalizedWidth,
            profile.NormalizedHeight);
        await WritePgmAsync(
            processedPath,
            profile.NormalizedWidth,
            profile.NormalizedHeight,
            normalized,
            cancellationToken).ConfigureAwait(false);
        MapDiagnosticSample sample = new(
            "1.0",
            label,
            profile.Id,
            profile.Version,
            result,
            image.SourceFrameSequence,
            image.SourceTimestamp,
            acceptedGameTime,
            image.Width,
            image.Height,
            captureLayout,
            Path.GetFileName(originalPath),
            Path.GetFileName(processedPath));
        await using FileStream stream = File.Create(Path.Combine(directory, "result.json"));
        await JsonSerializer.SerializeAsync(stream, sample, JsonOptions, cancellationToken).ConfigureAwait(false);
        return directory;
    }

    private static void WriteBmp(string path, MapImage image)
    {
        int rowBytes = checked(image.Width * 4);
        int pixelBytes = checked(rowBytes * image.Height);
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
        for (int row = 0; row < image.Height; row++)
        {
            stream.Write(image.BgraPixels.Span.Slice(row * image.Stride, rowBytes));
        }
    }

    private static async Task WritePgmAsync(
        string path,
        int width,
        int height,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.Create(path);
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P5\n{width} {height}\n255\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(pixels, cancellationToken).ConfigureAwait(false);
    }
}
