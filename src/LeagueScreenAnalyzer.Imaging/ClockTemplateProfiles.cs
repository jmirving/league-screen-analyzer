using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeagueScreenAnalyzer.Imaging;

public sealed record ClockGlyphProvenance(
    string SourceDiagnosticBundle,
    string ExplicitFullClockLabel,
    int CharacterPosition,
    string GlyphLabel,
    string PreprocessingProfileVersion,
    string? CapturedWithProfileId = null,
    int? CapturedWithProfileVersion = null,
    string? BuiltIntoProfile = null);

public sealed record ClockGlyphTemplateEntry(
    string TemplateId,
    string Glyph,
    string Image,
    ClockGlyphProvenance Provenance);

public sealed record ClockTemplateManifest(
    int SchemaVersion,
    string ProfileId,
    int ProfileVersion,
    string BaseProfileId,
    int TemplateWidth,
    int TemplateHeight,
    string PreprocessingVariant,
    IReadOnlyList<ClockGlyphTemplateEntry> Templates,
    IReadOnlyList<string> UnsupportedDigits);

public sealed record ClockGlyphTemplate(
    char Glyph,
    bool[] Pixels,
    string TemplateId,
    ClockGlyphProvenance Provenance);

public static class ClockTemplateProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static ClockTemplateManifest LoadManifest(string directory)
    {
        string path = Path.Combine(Path.GetFullPath(directory), "manifest.json");
        ClockTemplateManifest manifest = JsonSerializer.Deserialize<ClockTemplateManifest>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Template manifest is empty: {path}");
        ValidateManifest(manifest, directory);
        return manifest;
    }

    public static IReadOnlyList<ClockGlyphTemplate> LoadTemplates(string directory)
    {
        ClockTemplateManifest manifest = LoadManifest(directory);
        return manifest.Templates.Select(entry =>
        {
            (int width, int height, byte[] pixels) =
                ReadBinaryPgm(Path.Combine(directory, entry.Image));
            if (width != manifest.TemplateWidth || height != manifest.TemplateHeight)
            {
                throw new InvalidDataException(
                    $"Template '{entry.TemplateId}' dimensions {width}x{height} do not match manifest dimensions " +
                    $"{manifest.TemplateWidth}x{manifest.TemplateHeight}.");
            }

            return new ClockGlyphTemplate(
                entry.Glyph[0],
                pixels.Select(pixel => pixel != 0).ToArray(),
                entry.TemplateId,
                entry.Provenance);
        }).ToArray();
    }

    public static string FindProfileDirectory(string profileId)
    {
        if (TryFindProfileDirectory(profileId, out string? directory))
        {
            return directory!;
        }

        throw new DirectoryNotFoundException(
            $"Clock template profile '{profileId}' was not found beneath fixtures/clocks.");
    }

    public static bool TryFindProfileDirectory(string profileId, out string? directory)
    {
        ClockProfileCatalog catalog = ClockProfileCatalog.CreateDefault();
        if (catalog.TryGet(profileId, out ClockProfileCatalogEntry? entry) &&
            entry is not null &&
            entry.SourceManifestPath is not null)
        {
            directory = Path.GetDirectoryName(entry.SourceManifestPath);
            return true;
        }

        directory = null;
        return false;
    }

    public static void WriteManifest(string directory, ClockTemplateManifest manifest)
    {
        Directory.CreateDirectory(directory);
        ValidateManifest(manifest, directory, requireImages: false);
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine);
    }

    public static void ValidateManifest(
        ClockTemplateManifest manifest,
        string directory,
        bool requireImages = true)
    {
        if (manifest.SchemaVersion != 1 || manifest.ProfileVersion <= 0 ||
            string.IsNullOrWhiteSpace(manifest.ProfileId) ||
            string.IsNullOrWhiteSpace(manifest.BaseProfileId) ||
            manifest.TemplateWidth <= 0 || manifest.TemplateHeight <= 0 ||
            string.IsNullOrWhiteSpace(manifest.PreprocessingVariant) ||
            manifest.Templates is null || manifest.Templates.Count == 0 ||
            manifest.UnsupportedDigits is null)
        {
            throw new InvalidDataException("Clock template manifest header is malformed.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> images = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> assignments = new(StringComparer.Ordinal);
        foreach (ClockGlyphTemplateEntry entry in manifest.Templates)
        {
            ClockGlyphProvenance provenance = entry.Provenance
                ?? throw new InvalidDataException($"Template '{entry.TemplateId}' has no provenance.");
            if (string.IsNullOrWhiteSpace(entry.TemplateId) ||
                entry.Glyph.Length != 1 ||
                !(char.IsAsciiDigit(entry.Glyph[0]) || entry.Glyph[0] == ':') ||
                string.IsNullOrWhiteSpace(entry.Image) ||
                Path.IsPathRooted(entry.Image) ||
                entry.Image.Contains("..", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(provenance.SourceDiagnosticBundle) ||
                string.IsNullOrWhiteSpace(provenance.ExplicitFullClockLabel) ||
                provenance.CharacterPosition < 0 ||
                provenance.GlyphLabel != entry.Glyph ||
                string.IsNullOrWhiteSpace(provenance.PreprocessingProfileVersion))
            {
                throw new InvalidDataException(
                    $"Template '{entry.TemplateId}' has malformed provenance or identity.");
            }

            if (!ids.Add(entry.TemplateId) || !images.Add(entry.Image) ||
                !assignments.Add(
                    $"{provenance.SourceDiagnosticBundle}\0{provenance.CharacterPosition}\0{entry.Glyph}"))
            {
                throw new InvalidDataException(
                    $"Template '{entry.TemplateId}' duplicates a template identity, image, or glyph assignment.");
            }

            if (requireImages && !File.Exists(Path.Combine(directory, entry.Image)))
            {
                throw new InvalidDataException($"Template image is missing: {entry.Image}");
            }
        }
    }

    public static void WriteBinaryPgm(string path, bool[] pixels, int width, int height)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes($"P5\n{width} {height}\n255\n"));
        writer.Write(pixels.Select(pixel => pixel ? byte.MaxValue : byte.MinValue).ToArray());
    }

    private static (int Width, int Height, byte[] Pixels) ReadBinaryPgm(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int cursor = 0;
        string NextToken()
        {
            while (cursor < bytes.Length && char.IsWhiteSpace((char)bytes[cursor])) cursor++;
            int start = cursor;
            while (cursor < bytes.Length && !char.IsWhiteSpace((char)bytes[cursor])) cursor++;
            return System.Text.Encoding.ASCII.GetString(bytes, start, cursor - start);
        }

        if (NextToken() != "P5")
        {
            throw new InvalidDataException($"Template '{path}' is not a binary P5 PGM.");
        }

        int width = int.Parse(NextToken(), System.Globalization.CultureInfo.InvariantCulture);
        int height = int.Parse(NextToken(), System.Globalization.CultureInfo.InvariantCulture);
        if (NextToken() != "255")
        {
            throw new InvalidDataException($"Template '{path}' must use maximum value 255.");
        }

        while (cursor < bytes.Length && char.IsWhiteSpace((char)bytes[cursor])) cursor++;
        if (bytes.Length - cursor != checked(width * height))
        {
            throw new InvalidDataException($"Template '{path}' has invalid pixel length.");
        }

        return (width, height, bytes[cursor..]);
    }
}

public static class ClockTemplateMatcher
{
    public static bool[] Normalize(byte[] pixels, int width, int height, int outputWidth, int outputHeight)
    {
        bool[] output = new bool[outputWidth * outputHeight];
        for (int y = 0; y < outputHeight; y++)
        {
            int top = y * height / outputHeight;
            int bottom = Math.Max(top + 1, (y + 1) * height / outputHeight);
            for (int x = 0; x < outputWidth; x++)
            {
                int left = x * width / outputWidth;
                int right = Math.Max(left + 1, (x + 1) * width / outputWidth);
                int foreground = 0;
                int total = 0;
                for (int sy = top; sy < Math.Min(height, bottom); sy++)
                for (int sx = left; sx < Math.Min(width, right); sx++)
                {
                    total++;
                    if (pixels[(sy * width) + sx] != 0) foreground++;
                }

                output[(y * outputWidth) + x] = foreground * 2 >= total;
            }
        }

        return output;
    }

    public static double SimilarityWithTranslation(
        bool[] candidate,
        bool[] template,
        int width,
        int height,
        int tolerance = 1)
    {
        double best = 0;
        for (int dy = -tolerance; dy <= tolerance; dy++)
        for (int dx = -tolerance; dx <= tolerance; dx++)
        {
            int intersection = 0, union = 0, candidateCount = 0, templateCount = 0, equal = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool left = candidate[(y * width) + x];
                int tx = x - dx;
                int ty = y - dy;
                bool right = tx >= 0 && tx < width && ty >= 0 && ty < height &&
                             template[(ty * width) + tx];
                if (left) candidateCount++;
                if (right) templateCount++;
                if (left && right) intersection++;
                if (left || right) union++;
                if (left == right) equal++;
            }

            double dice = candidateCount + templateCount == 0
                ? 0 : 2d * intersection / (candidateCount + templateCount);
            double iou = union == 0 ? 0 : (double)intersection / union;
            double agreement = (double)equal / (width * height);
            double score = (0.5 * dice) + (0.3 * iou) + (0.2 * agreement)
                         - (0.005 * (Math.Abs(dx) + Math.Abs(dy)));
            best = Math.Max(best, score);
        }

        return Math.Clamp(best, 0, 1);
    }
}
