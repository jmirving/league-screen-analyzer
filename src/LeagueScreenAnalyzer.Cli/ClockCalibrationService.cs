using System.Text;
using System.Text.Json;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Cli;

public sealed record ClockCoverageReport(
    IReadOnlyDictionary<string, int> DigitOccurrences,
    IReadOnlyDictionary<string, int> DigitSourceBundles,
    int SeparatorExamples,
    int SingleDigitMinuteSamples,
    int DoubleDigitMinuteSamples,
    int MinuteBoundarySamples,
    IReadOnlyDictionary<string, int> CropSizes,
    string SourceWindowResolutionAvailability,
    IReadOnlyDictionary<string, int> SourceLuminanceRanges,
    IReadOnlyDictionary<string, int> PreprocessingVariants,
    IReadOnlyList<string> UnsupportedDigits,
    IReadOnlyList<string> WeaklySupportedDigits,
    IReadOnlyList<string> RecommendedAdditionalLabels);

public sealed record ClockCalibrationAnalysisReport(
    int SampleCount,
    IReadOnlyList<ClockCalibrationSampleAnalysis> Samples,
    ClockCoverageReport Coverage,
    IReadOnlyList<ClockPreprocessingVariantEvaluation> PreprocessingVariantEvaluations);

public sealed record ClockPreprocessingVariantEvaluation(
    string Variant,
    int AlignedSegmentations,
    IReadOnlyDictionary<string, int> RecognitionStatuses,
    double MeanBestCandidateConfidence);

public sealed record ClockCalibrationSampleAnalysis(
    string Id,
    string ExplicitLabel,
    string ExpectedDisplaySequence,
    string? RecognizedCandidate,
    string FinalStatus,
    string? RejectionReason,
    int SegmentationCount,
    int ExpectedCharacterCount,
    IReadOnlyList<string> BestMatches,
    bool PreprocessingVisuallyCorrect,
    string RootCauseCategory,
    string PreprocessingVariant);

public sealed record ClockProfileBuildCompatibility(
    string Sample,
    bool Accepted,
    string? RejectionReason,
    string CapturedWithProfile,
    string BuiltIntoProfile);

public sealed class ClockCalibrationService
{
    private const int TemplateWidth = 12;
    private const int TemplateHeight = 16;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<ClockCalibrationAnalysisReport> AnalyzeAsync(
        string profileId,
        string diagnosticsRoot,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DiagnosticSample> samples = await LoadSamplesAsync(
            diagnosticsRoot, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(outputDirectory);
        string segmentationDirectory = Path.Combine(outputDirectory, "segmentation-annotations");
        Directory.CreateDirectory(segmentationDirectory);

        List<ClockCalibrationSampleAnalysis> rows = [];
        foreach (DiagnosticSample sample in samples)
        {
            string display = AlignDisplaySequence(sample);
            bool visuallyCorrect = sample.Segments.Count == display.Length &&
                                   sample.PreprocessingVariant == "Otsu/LightOnDark";
            string rootCause = sample.Candidate is not null &&
                               !sample.Candidate.Contains(':', StringComparison.Ordinal)
                ? "separator-recognition-and-template-similarity"
                : "template-similarity";
            rows.Add(new ClockCalibrationSampleAnalysis(
                sample.Id,
                sample.Label.Value,
                display,
                sample.Candidate,
                sample.Status,
                sample.Reason,
                sample.Segments.Count,
                display.Length,
                sample.BestMatches,
                visuallyCorrect,
                rootCause,
                sample.PreprocessingVariant));
            await File.WriteAllTextAsync(
                Path.Combine(segmentationDirectory, $"{sample.Id}.svg"),
                CreateSegmentationSvg(sample, display),
                cancellationToken).ConfigureAwait(false);
        }

        ClockCoverageReport coverage = CreateCoverage(samples);
        IReadOnlyList<ClockPreprocessingVariantEvaluation> variantEvaluations =
            await EvaluatePreprocessingVariantsAsync(samples, cancellationToken).ConfigureAwait(false);
        ClockCalibrationAnalysisReport report = new(
            samples.Count, rows, coverage, variantEvaluations);
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "clock-calibration-analysis.json"),
            report,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "glyph-coverage.json"),
            coverage,
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "per-sample-failure-analysis.md"),
            CreateMarkdown(rows),
            cancellationToken).ConfigureAwait(false);
        return report;
    }

    public async Task<ClockTemplateManifest> BuildProfileAsync(
        string baseProfileId,
        string profileId,
        string diagnosticsRoot,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (profileId == baseProfileId)
        {
            throw new ArgumentException("A generated profile must not overwrite its base profile.");
        }

        IReadOnlyList<DiagnosticSample> sourceSamples = await LoadSamplesAsync(
            diagnosticsRoot, cancellationToken).ConfigureAwait(false);
        ClockRecognitionProfile baseProfile = BuiltInClockProfiles.Get(baseProfileId);
        Directory.CreateDirectory(outputDirectory);
        List<ClockGlyphTemplateEntry> entries = [];
        List<DiagnosticSample> acceptedSamples = [];
        List<ClockProfileBuildCompatibility> compatibility = [];
        foreach ((DiagnosticSample source, int index) in
                 sourceSamples.Select((sample, index) => (sample, index)))
        {
            DiagnosticSample sample;
            string display;
            try
            {
                if (!File.Exists(source.ImagePath))
                {
                    throw new FileNotFoundException("Original CLOCK crop is missing.", source.ImagePath);
                }

                using ClockImage image = LoadBitmap(
                    source.ImagePath, index, TimeSpan.FromSeconds(index));
                ClockRecognitionResult current = await new ConstrainedClockImageRecognizer()
                    .RecognizeAsync(image, baseProfile, cancellationToken)
                    .ConfigureAwait(false);
                sample = source with
                {
                    PreprocessingVariant = current.Diagnostics.PreprocessingVariant,
                    Width = current.Diagnostics.NormalizedWidth,
                    Height = current.Diagnostics.NormalizedHeight,
                    Segments = current.Diagnostics.Segments
                };
                display = AlignDisplaySequence(sample);
                ValidateAlignment(sample, display);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or OverflowException)
            {
                compatibility.Add(new ClockProfileBuildCompatibility(
                    source.Id,
                    false,
                    exception.Message,
                    $"{source.CapturedWithProfileId} v{source.CapturedWithProfileVersion}",
                    profileId));
                continue;
            }

            acceptedSamples.Add(sample);
            compatibility.Add(new ClockProfileBuildCompatibility(
                sample.Id,
                true,
                null,
                $"{sample.CapturedWithProfileId} v{sample.CapturedWithProfileVersion}",
                profileId));
            for (int position = 0; position < display.Length; position++)
            {
                char glyph = display[position];
                ClockSegment segment = sample.Segments[position];
                bool[] normalized = ClockTemplateMatcher.Normalize(
                    segment.Pixels,
                    segment.Width,
                    segment.Height,
                    TemplateWidth,
                    TemplateHeight);
                string glyphDirectory = glyph == ':' ? "separator" : glyph.ToString();
                string relative = Path.Combine(
                    "templates",
                    glyphDirectory,
                    $"{sample.Id}-p{position:00}.pgm").Replace('\\', '/');
                string fullPath = Path.Combine(outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                ClockTemplateProfileLoader.WriteBinaryPgm(
                    fullPath, normalized, TemplateWidth, TemplateHeight);
                string templateId = $"{glyphDirectory}-{sample.Id}-p{position:00}";
                entries.Add(new ClockGlyphTemplateEntry(
                    templateId,
                    glyph.ToString(),
                    relative,
                    new ClockGlyphProvenance(
                        sample.Id,
                        sample.Label.Value,
                        position,
                        glyph.ToString(),
                        $"{profileId}/{sample.PreprocessingVariant}",
                        sample.CapturedWithProfileId,
                        sample.CapturedWithProfileVersion,
                        profileId)));
            }
        }

        if (acceptedSamples.Count == 0)
        {
            await WriteJsonAsync(
                Path.Combine(outputDirectory, "compatibility.json"),
                compatibility,
                cancellationToken).ConfigureAwait(false);
            throw new InvalidDataException(
                "No labeled diagnostic sample could be aligned using the requested base profile.");
        }

        InheritUnreplacedTemplates(baseProfileId, outputDirectory, entries);
        ClockCoverageReport coverage = CreateCoverage(acceptedSamples);
        ClockTemplateManifest manifest = new(
            1,
            profileId,
            baseProfile.Version + 1,
            baseProfileId,
            TemplateWidth,
            TemplateHeight,
            "integer-bt709-luminance/otsu/light-on-dark/column-projection/12x16-majority",
            entries.OrderBy(entry => entry.TemplateId, StringComparer.Ordinal).ToArray(),
            coverage.UnsupportedDigits);
        ClockTemplateProfileLoader.WriteManifest(outputDirectory, manifest);
        _ = ClockTemplateProfileLoader.LoadTemplates(outputDirectory);
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "coverage.json"),
            coverage,
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(outputDirectory, "compatibility.json"),
            compatibility,
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "README.md"),
            $"# {profileId}\n\nDeterministically generated from {acceptedSamples.Count} explicitly labeled League replay diagnostic bundles " +
            $"using {baseProfileId} preprocessing and settings. Capture profile IDs are retained only as provenance. " +
            "Regenerate with `build-clock-profile`; do not edit template pixels by hand.\n",
            cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    private static void InheritUnreplacedTemplates(
        string baseProfileId,
        string outputDirectory,
        List<ClockGlyphTemplateEntry> entries)
    {
        if (!ClockTemplateProfileLoader.TryFindProfileDirectory(
                baseProfileId, out string? baseDirectory))
        {
            return;
        }

        ClockTemplateManifest baseManifest =
            ClockTemplateProfileLoader.LoadManifest(baseDirectory!);
        HashSet<string> generatedAssignments = entries
            .Select(TemplateAssignment)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ClockGlyphTemplateEntry inherited in baseManifest.Templates
                     .Where(entry => !generatedAssignments.Contains(TemplateAssignment(entry)))
                     .OrderBy(entry => entry.TemplateId, StringComparer.Ordinal))
        {
            string relative = Path.Combine(
                    "templates", "inherited", inherited.Image.Replace('/', Path.DirectorySeparatorChar))
                .Replace('\\', '/');
            string destination = Path.Combine(
                outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(baseDirectory!, inherited.Image), destination, overwrite: true);
            entries.Add(inherited with
            {
                TemplateId = $"inherited-{inherited.TemplateId}",
                Image = relative
            });
        }
    }

    private static string TemplateAssignment(ClockGlyphTemplateEntry entry) =>
        $"{entry.Provenance.SourceDiagnosticBundle}\0" +
        $"{entry.Provenance.CharacterPosition}\0{entry.Glyph}";

    internal static async Task<IReadOnlyList<DiagnosticSample>> LoadSamplesAsync(
        string diagnosticsRoot,
        CancellationToken cancellationToken)
    {
        string fullRoot = Path.GetFullPath(diagnosticsRoot);
        List<DiagnosticSample> samples = [];
        foreach (string resultPath in Directory.EnumerateFiles(
                     fullRoot, "result.json", SearchOption.AllDirectories)
                 .OrderBy(path => Path.GetRelativePath(fullRoot, path), StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(
                await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("explicitLabel", out JsonElement labelElement) ||
                labelElement.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (!root.TryGetProperty("schemaVersion", out JsonElement schema) ||
                schema.GetInt32() != 2)
            {
                throw new InvalidDataException(
                    $"Unsupported diagnostic schema in '{resultPath}'.");
            }

            JsonElement profile = root.GetProperty("profile");
            string bundleProfile = profile.GetProperty("id").GetString()
                ?? throw new InvalidDataException($"Capture profile id is missing in '{resultPath}'.");
            int bundleProfileVersion = profile.GetProperty("version").GetInt32();
            if (string.IsNullOrWhiteSpace(bundleProfile) || bundleProfileVersion <= 0)
            {
                throw new InvalidDataException($"Capture profile provenance is malformed in '{resultPath}'.");
            }

            if (!ClockSampleLabelParser.TryParse(
                    labelElement.GetString(), out ClockSampleLabel? label, out string? reason))
            {
                throw new InvalidDataException($"Invalid explicit label in '{resultPath}': {reason}");
            }

            JsonElement recognition = root.GetProperty("recognition");
            JsonElement diagnostics = recognition.GetProperty("diagnostics");
            List<ClockSegment> segments = diagnostics.GetProperty("segments")
                .EnumerateArray()
                .Select(segment => new ClockSegment(
                    segment.GetProperty("x").GetInt32(),
                    segment.GetProperty("y").GetInt32(),
                    segment.GetProperty("width").GetInt32(),
                    segment.GetProperty("height").GetInt32(),
                    segment.GetProperty("pixels").GetBytesFromBase64()))
                .ToList();
            string id = Path.GetRelativePath(fullRoot, Path.GetDirectoryName(resultPath)!)
                .Replace('\\', '/');
            JsonElement best = recognition.TryGetProperty("bestCandidate", out JsonElement bestElement)
                ? bestElement
                : default;
            List<string> matches = best.ValueKind == JsonValueKind.Object
                ? best.GetProperty("characters").EnumerateArray()
                    .Select(item =>
                        $"{item.GetProperty("character").GetString()}:{item.GetProperty("confidence").GetDouble():0.000}")
                    .ToList()
                : [];
            samples.Add(new DiagnosticSample(
                id,
                Path.Combine(Path.GetDirectoryName(resultPath)!, "original-clock.bmp"),
                label!,
                root.GetProperty("reading").GetProperty("rawRecognizedText").GetString(),
                root.GetProperty("reading").GetProperty("status").GetString()!,
                root.GetProperty("reading").GetProperty("diagnosticReason").GetString(),
                diagnostics.GetProperty("preprocessingVariant").GetString()!,
                diagnostics.GetProperty("normalizedWidth").GetInt32(),
                diagnostics.GetProperty("normalizedHeight").GetInt32(),
                segments,
                matches,
                bundleProfile,
                bundleProfileVersion));
        }

        if (samples.Count == 0)
        {
            throw new InvalidDataException("No explicitly labeled diagnostic bundles were found.");
        }

        return samples;
    }

    internal static string AlignDisplaySequence(DiagnosticSample sample)
        => AlignDisplaySequence(sample.Label.Value, sample.Segments.Count);

    public static string AlignDisplaySequence(string explicitLabel, int segmentCount)
    {
        if (!ClockSampleLabelParser.TryParse(
                explicitLabel, out ClockSampleLabel? parsed, out string? reason))
        {
            throw new InvalidDataException($"Invalid explicit label: {reason}");
        }

        string label = parsed!.Value;
        if (segmentCount == label.Length)
        {
            return label;
        }

        if (segmentCount == 5 && label.Length == 4)
        {
            return $"0{label}";
        }

        throw new InvalidDataException(
            $"Ambiguous segment-to-label alignment: label '{label}' has {label.Length} characters " +
            $"but segmentation has {segmentCount}. Template extraction stopped.");
    }

    private static void ValidateAlignment(DiagnosticSample sample, string display)
    {
        if (sample.Segments.Count != display.Length)
        {
            throw new InvalidDataException($"Segment alignment failed for '{sample.Id}'.");
        }

        int separator = display.IndexOf(':');
        ClockSegment colon = sample.Segments[separator];
        int foreground = colon.Pixels.Count(pixel => pixel != 0);
        int widestDigit = sample.Segments
            .Where((_, index) => index != separator)
            .Max(segment => segment.Width);
        if (colon.Width * 2 > widestDigit || foreground == 0 ||
            !HasTwoForegroundRowGroups(colon))
        {
            throw new InvalidDataException(
                $"Ambiguous separator alignment for '{sample.Id}' at position {separator}. Template extraction stopped.");
        }
    }

    private static bool HasTwoForegroundRowGroups(ClockSegment segment)
    {
        int groups = 0;
        bool active = false;
        for (int y = 0; y < segment.Height; y++)
        {
            bool row = Enumerable.Range(0, segment.Width)
                .Any(x => segment.Pixels[(y * segment.Width) + x] != 0);
            if (row && !active) groups++;
            active = row;
        }

        return groups == 2;
    }

    private static ClockCoverageReport CreateCoverage(IReadOnlyList<DiagnosticSample> samples)
    {
        Dictionary<string, int> occurrences = Enumerable.Range(0, 10)
            .ToDictionary(value => value.ToString(), _ => 0, StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> sources = Enumerable.Range(0, 10)
            .ToDictionary(value => value.ToString(), _ => new HashSet<string>(StringComparer.Ordinal));
        Dictionary<string, int> sizes = new(StringComparer.Ordinal);
        Dictionary<string, int> luminanceRanges = new(StringComparer.Ordinal);
        Dictionary<string, int> preprocessingVariants = new(StringComparer.Ordinal);
        int separators = 0, single = 0, doubleMinutes = 0, boundary = 0;
        foreach (DiagnosticSample sample in samples)
        {
            string display = AlignDisplaySequence(sample);
            foreach (char glyph in display)
            {
                if (char.IsAsciiDigit(glyph))
                {
                    string key = glyph.ToString();
                    occurrences[key]++;
                    sources[key].Add(sample.Id);
                }
                else if (glyph == ':') separators++;
            }

            if (sample.Label.Value.IndexOf(':') == 1) single++; else doubleMinutes++;
            if (sample.Label.Value.EndsWith(":00", StringComparison.Ordinal) ||
                sample.Label.Value.EndsWith(":59", StringComparison.Ordinal)) boundary++;
            string size = $"{sample.Width}x{sample.Height}";
            sizes[size] = sizes.GetValueOrDefault(size) + 1;
            string luminanceRange = ReadLuminanceRange(sample.ImagePath);
            luminanceRanges[luminanceRange] = luminanceRanges.GetValueOrDefault(luminanceRange) + 1;
            preprocessingVariants[sample.PreprocessingVariant] =
                preprocessingVariants.GetValueOrDefault(sample.PreprocessingVariant) + 1;
        }

        string[] unsupported = occurrences.Where(pair => pair.Value == 0).Select(pair => pair.Key).ToArray();
        string[] weak = sources.Where(pair => pair.Value.Count is > 0 and < 2)
            .Select(pair => pair.Key).ToArray();
        List<string> recommendations = [];
        if (unsupported.Contains("8")) recommendations.Add("8:18");
        if (weak.Contains("4")) recommendations.Add("4:46");
        if (weak.Contains("6")) recommendations.Add("6:16");
        if (weak.Contains("7")) recommendations.Add("7:47");
        recommendations.Add("10:08");
        return new ClockCoverageReport(
            occurrences,
            sources.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal),
            separators,
            single,
            doubleMinutes,
            boundary,
            sizes,
            "Not recorded in schemaVersion 2 diagnostic bundles",
            luminanceRanges,
            preprocessingVariants,
            unsupported,
            weak,
            recommendations.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static async Task<IReadOnlyList<ClockPreprocessingVariantEvaluation>>
        EvaluatePreprocessingVariantsAsync(
            IReadOnlyList<DiagnosticSample> samples,
            CancellationToken cancellationToken)
    {
        ClockRecognitionProfile baseline =
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id);
        (string Name, ClockRecognitionProfile Profile)[] variants =
        [
            ("Otsu/LightOnDark", baseline),
            ("Fixed160/LightOnDark", baseline with
            {
                ThresholdStrategy = ClockThresholdStrategy.Fixed,
                FixedThreshold = 160
            }),
            ("Otsu/DarkOnLight", baseline with
            {
                ForegroundPolarity = ClockForegroundPolarity.DarkOnLight
            })
        ];
        List<ClockPreprocessingVariantEvaluation> reports = [];
        foreach ((string name, ClockRecognitionProfile profile) in variants)
        {
            int aligned = 0;
            double confidence = 0;
            Dictionary<string, int> statuses = new(StringComparer.Ordinal);
            foreach ((DiagnosticSample sample, int index) in
                     samples.Select((sample, index) => (sample, index)))
            {
                using ClockImage image = LoadBitmap(
                    sample.ImagePath, index, TimeSpan.FromSeconds(index));
                ClockRecognitionResult recognition =
                    await new ConstrainedClockImageRecognizer()
                        .RecognizeAsync(image, profile, cancellationToken)
                        .ConfigureAwait(false);
                if (recognition.Diagnostics.Segments.Count ==
                    AlignDisplaySequence(sample).Length)
                {
                    aligned++;
                }

                string status = recognition.Status.ToString();
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
                confidence += recognition.Confidence;
            }

            reports.Add(new ClockPreprocessingVariantEvaluation(
                name,
                aligned,
                statuses,
                confidence / samples.Count));
        }

        return reports;
    }

    private static string ReadLuminanceRange(string path)
    {
        using ClockImage image = LoadBitmap(path, 0, TimeSpan.Zero);
        ReadOnlySpan<byte> pixels = image.BgraPixels.Span;
        byte minimum = byte.MaxValue;
        byte maximum = byte.MinValue;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            int index = (y * image.Stride) + (x * 4);
            byte value = (byte)(
                ((pixels[index + 2] * 54) +
                 (pixels[index + 1] * 183) +
                 (pixels[index] * 19)) >> 8);
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        return $"{minimum}-{maximum}";
    }

    private static ClockImage LoadBitmap(
        string path,
        long sequence,
        TimeSpan timestamp)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);
        if (reader.ReadUInt16() != 0x4D42)
        {
            throw new InvalidDataException($"'{path}' is not a BMP image.");
        }

        stream.Position = 10;
        int pixelOffset = reader.ReadInt32();
        int dibSize = reader.ReadInt32();
        int width = reader.ReadInt32();
        int storedHeight = reader.ReadInt32();
        stream.Position = 26;
        ushort planes = reader.ReadUInt16();
        ushort bitsPerPixel = reader.ReadUInt16();
        uint compression = reader.ReadUInt32();
        if (dibSize < 40 || width <= 0 || storedHeight == 0 ||
            planes != 1 || bitsPerPixel != 32 || compression != 0)
        {
            throw new InvalidDataException($"'{path}' must be an uncompressed 32-bit BMP.");
        }

        int height = Math.Abs(storedHeight);
        int stride = checked(width * 4);
        byte[] bgra = new byte[checked(stride * height)];
        stream.Position = pixelOffset;
        for (int row = 0; row < height; row++)
        {
            int destination = storedHeight > 0 ? height - 1 - row : row;
            if (reader.Read(bgra, destination * stride, stride) != stride)
            {
                throw new InvalidDataException($"'{path}' has truncated pixel data.");
            }
        }

        return new ClockImage(width, height, stride, bgra, sequence, timestamp);
    }

    private static string CreateSegmentationSvg(DiagnosticSample sample, string display)
    {
        const int scale = 8;
        StringBuilder svg = new();
        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{sample.Width * scale}\" height=\"{(sample.Height * scale) + 28}\" viewBox=\"0 0 {sample.Width} {sample.Height + 4}\">");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#101820\"/>");
        foreach ((ClockSegment segment, int index) in sample.Segments.Select((segment, index) => (segment, index)))
        {
            svg.AppendLine($"<rect x=\"{segment.X}\" y=\"{segment.Y}\" width=\"{segment.Width}\" height=\"{segment.Height}\" fill=\"none\" stroke=\"#ffcc00\" stroke-width=\"0.25\"/>");
            svg.AppendLine($"<text x=\"{segment.X + (segment.Width / 2d):0.##}\" y=\"{sample.Height + 3}\" text-anchor=\"middle\" fill=\"white\" font-size=\"2.5\">{display[index]}</text>");
        }
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static string CreateMarkdown(IReadOnlyList<ClockCalibrationSampleAnalysis> rows)
    {
        StringBuilder text = new("# Per-sample clock calibration failure analysis\n\n");
        text.AppendLine("| Sample | Label | Display | Candidate | Status | Reason | Segments | Best matches | Preprocessing | Root cause |");
        text.AppendLine("|---|---:|---:|---:|---|---|---:|---|---|---|");
        foreach (ClockCalibrationSampleAnalysis row in rows)
        {
            text.AppendLine(
                $"| {row.Id} | {row.ExplicitLabel} | {row.ExpectedDisplaySequence} | {row.RecognizedCandidate} | " +
                $"{row.FinalStatus} | {row.RejectionReason} | {row.SegmentationCount}/{row.ExpectedCharacterCount} | " +
                $"{string.Join(" ", row.BestMatches)} | {(row.PreprocessingVisuallyCorrect ? "correct" : "review")} | {row.RootCauseCategory} |");
        }
        return text.ToString();
    }

    private static Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine,
            cancellationToken);
}

internal sealed record DiagnosticSample(
    string Id,
    string ImagePath,
    ClockSampleLabel Label,
    string? Candidate,
    string Status,
    string? Reason,
    string PreprocessingVariant,
    int Width,
    int Height,
    IReadOnlyList<ClockSegment> Segments,
    IReadOnlyList<string> BestMatches,
    string CapturedWithProfileId,
    int CapturedWithProfileVersion);
