using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Cli;

public sealed record ClockEvaluationManifest(
    string ProfileId,
    string Provenance,
    IReadOnlyList<ClockEvaluationSample> Samples);

public sealed record ClockEvaluationSample(
    string Id,
    string Image,
    string? Label,
    bool ExpectedAccepted);

public sealed record ClockSampleEvaluation(
    string Id,
    string? Label,
    string? RecognizedText,
    ClockReadingStatus Status,
    double Confidence,
    bool ExactMatch,
    string? Reason,
    ClockReadingStatus VisualStatus,
    ClockReadingStatus FinalStatus,
    string PreprocessingVariant,
    bool OwnTemplatesExcluded,
    IReadOnlyList<ClockCharacterCandidate> BestCharacters,
    string? CapturedWithProfile,
    string EvaluatedWithProfile,
    string? OriginalCandidate,
    string? OriginalStatus,
    string? OriginalPreprocessingVariant,
    string? NewCandidate,
    ClockReadingStatus NewStatus);

public sealed record ClockSampleCompatibility(
    string Sample,
    bool Accepted,
    string? RejectionReason,
    string? CapturedWithProfile,
    string EvaluatedWithProfile);

public sealed record ClockEvaluationReport(
    int TotalSamples,
    int CorrectlyAccepted,
    int CorrectlyRejected,
    int FalseAccepts,
    int FalseRejects,
    double CharacterAccuracy,
    double FullClockExactMatchAccuracy,
    IReadOnlyDictionary<string, int> ConfidenceDistribution,
    IReadOnlyDictionary<string, int> ConfusionCounts,
    string Provenance,
    IReadOnlyList<ClockSampleEvaluation> Samples,
    string EvaluationKind,
    int VisualRejections,
    int TemporalRejections,
    IReadOnlyList<string> FalseAcceptIds,
    IReadOnlyList<string> FalseRejectIds,
    int RejectedSamples,
    IReadOnlyList<ClockSampleCompatibility> Compatibility);

public sealed class ClockEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ClockEvaluationReport> EvaluateAsync(
        string profileId,
        string manifestPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        string fullManifestPath = Path.GetFullPath(manifestPath);
        await using FileStream stream = File.OpenRead(fullManifestPath);
        ClockEvaluationManifest manifest =
            await JsonSerializer.DeserializeAsync<ClockEvaluationManifest>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("Clock evaluation manifest is empty.", nameof(manifestPath));
        if (!string.Equals(profileId, manifest.ProfileId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Manifest profile '{manifest.ProfileId}' does not match requested profile '{profileId}'.");
        }

        string baseDirectory = Path.GetDirectoryName(fullManifestPath)!;
        IReadOnlyList<EvaluationInput> samples = manifest.Samples
            .Select(sample => new EvaluationInput(
                sample.Id,
                Path.GetFullPath(Path.Combine(baseDirectory, sample.Image)),
                sample.Label,
                sample.ExpectedAccepted,
                null,
                null,
                null,
                null,
                null))
            .ToArray();
        return await EvaluateSamplesAsync(
            profileId,
            samples,
            manifest.Provenance,
            outputDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClockEvaluationReport> EvaluateDiagnosticBundlesAsync(
        string profileId,
        string diagnosticRoot,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        string fullRoot = Path.GetFullPath(diagnosticRoot);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(
                $"Clock diagnostic directory was not found: {fullRoot}");
        }

        List<EvaluationInput> samples = [];
        List<ClockSampleCompatibility> compatibility = [];
        IEnumerable<string> resultPaths = Directory
            .EnumerateFiles(fullRoot, "result.json", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(fullRoot, path), StringComparer.Ordinal);
        foreach (string resultPath in resultPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string bundleDirectory = Path.GetDirectoryName(resultPath)!;
            string sampleId = Path.GetRelativePath(fullRoot, bundleDirectory).Replace('\\', '/');
            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false));
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("schemaVersion", out JsonElement schema) ||
                    schema.ValueKind != JsonValueKind.Number ||
                    schema.GetInt32() != 2)
                {
                    throw new InvalidDataException("Unsupported diagnostic schema.");
                }

                JsonElement profile = root.GetProperty("profile");
                string bundleProfileId = profile.GetProperty("id").GetString()
                    ?? throw new InvalidDataException("Capture profile id is missing.");
                int bundleProfileVersion = profile.GetProperty("version").GetInt32();
                if (string.IsNullOrWhiteSpace(bundleProfileId) || bundleProfileVersion <= 0)
                {
                    throw new InvalidDataException("Capture profile provenance is malformed.");
                }

                string capturedWith = $"{bundleProfileId} v{bundleProfileVersion}";
                if (!root.TryGetProperty("explicitLabel", out JsonElement labelElement) ||
                    labelElement.ValueKind == JsonValueKind.Null)
                {
                    compatibility.Add(new ClockSampleCompatibility(
                        sampleId, false, "Sample was explicitly saved as unlabeled.",
                        capturedWith, profileId));
                    continue;
                }

                string? rawLabel = labelElement.GetString();
                if (!ClockSampleLabelParser.TryParse(
                        rawLabel,
                        out ClockSampleLabel? parsedLabel,
                        out string? validationMessage))
                {
                    throw new InvalidDataException($"Malformed explicit label: {validationMessage}");
                }

                string imagePath = Path.Combine(bundleDirectory, "original-clock.bmp");
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException("Original CLOCK crop is missing.", imagePath);
                }
                using (ClockImage decoded = LoadClockImage(imagePath, 0, TimeSpan.Zero))
                {
                    decoded.Validate();
                }

                JsonElement reading = root.GetProperty("reading");
                samples.Add(new EvaluationInput(
                    sampleId,
                    imagePath,
                    parsedLabel!.Value,
                    ExpectedAccepted: true,
                    SourceDiagnosticBundle: sampleId,
                    CapturedWithProfile: capturedWith,
                    OriginalCandidate: reading.GetProperty("rawRecognizedText").GetString(),
                    OriginalStatus: reading.GetProperty("status").GetString(),
                    OriginalPreprocessingVariant: root.GetProperty("recognition")
                        .GetProperty("diagnostics")
                        .GetProperty("preprocessingVariant")
                        .GetString()));
                compatibility.Add(new ClockSampleCompatibility(
                    sampleId, true, null, capturedWith, profileId));
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or KeyNotFoundException or
                    JsonException or InvalidOperationException or OverflowException)
            {
                compatibility.Add(new ClockSampleCompatibility(
                    sampleId, false, exception.Message, null, profileId));
            }
        }

        if (samples.Count == 0 && compatibility.Count == 0)
        {
            throw new InvalidDataException(
                $"No labeled clock diagnostic bundles were found under '{fullRoot}'.");
        }

        ClockEvaluationReport training = await EvaluateSamplesAsync(
            profileId,
            samples,
            $"Human-labeled clock diagnostic bundles discovered under {fullRoot}",
            outputDirectory,
            cancellationToken,
            excludeOwnTemplates: false,
            reportFileName: "clock-evaluation.json",
            evaluationKind: "apparent-training-set",
            compatibility: compatibility).ConfigureAwait(false);
        if (ClockTemplateProfileLoader.TryFindProfileDirectory(profileId, out _))
        {
            await EvaluateSamplesAsync(
                profileId,
                samples,
                $"Leave-one-sample-out over human-labeled clock diagnostic bundles under {fullRoot}",
                outputDirectory,
                cancellationToken,
                excludeOwnTemplates: true,
                reportFileName: "clock-evaluation-leave-one-out.json",
                evaluationKind: "leave-one-sample-out",
                compatibility: compatibility).ConfigureAwait(false);
        }

        return training;
    }

    private static async Task<ClockEvaluationReport> EvaluateSamplesAsync(
        string profileId,
        IReadOnlyList<EvaluationInput> samples,
        string provenance,
        string outputDirectory,
        CancellationToken cancellationToken,
        bool excludeOwnTemplates = false,
        string reportFileName = "clock-evaluation.json",
        string evaluationKind = "standard",
        IReadOnlyList<ClockSampleCompatibility>? compatibility = null)
    {
        ClockRecognitionProfile profile = BuiltInClockProfiles.Get(profileId);
        ConstrainedClockImageRecognizer recognizer = new();
        ClockTemporalValidator validator = new();
        List<ClockSampleEvaluation> results = [];
        Dictionary<string, int> confidenceDistribution = new(StringComparer.Ordinal)
        {
            ["0.00-0.49"] = 0,
            ["0.50-0.74"] = 0,
            ["0.75-0.87"] = 0,
            ["0.88-1.00"] = 0
        };
        Dictionary<string, int> confusion = new(StringComparer.Ordinal);
        int correctlyAccepted = 0;
        int correctlyRejected = 0;
        int falseAccepts = 0;
        int falseRejects = 0;
        int characterMatches = 0;
        int characterTotal = 0;
        int exactMatches = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvaluationInput sample = samples[i];
            using ClockImage image = LoadClockImage(
                sample.ImagePath,
                i,
                TimeSpan.FromSeconds(i));
            ClockRecognitionResult recognition =
                await recognizer.RecognizeAsync(
                    image,
                    profile,
                    new ClockRecognitionOptions(
                        excludeOwnTemplates ? sample.SourceDiagnosticBundle : null),
                    cancellationToken).ConfigureAwait(false);
            validator.Reset();
            ClockReading reading = validator.Validate(recognition, profile, i, image.SourceTimestamp);
            bool accepted = reading.Status == ClockReadingStatus.Valid;
            bool exact = accepted && sample.Label is not null &&
                         ClockSampleLabelParser.TryParse(
                             sample.Label, out ClockSampleLabel? expectedLabel, out _) &&
                         reading.BestCandidate?.ParsedGameTime ==
                         TimeSpan.FromSeconds(expectedLabel!.TotalSeconds);

            if (sample.ExpectedAccepted)
            {
                if (exact)
                {
                    correctlyAccepted++;
                    exactMatches++;
                }
                else if (accepted)
                {
                    falseAccepts++;
                }
                else
                {
                    falseRejects++;
                }
            }
            else if (accepted)
            {
                falseAccepts++;
            }
            else
            {
                correctlyRejected++;
            }

            AddConfidenceBucket(confidenceDistribution, reading.Confidence);
            if (sample.Label is not null)
            {
                string recognized = reading.RawRecognizedText ?? string.Empty;
                string expectedText = sample.Label;
                if (recognized.Length == 5 && expectedText.Length == 4)
                {
                    expectedText = $"0{expectedText}";
                }

                int length = Math.Max(expectedText.Length, recognized.Length);
                characterTotal += length;
                for (int characterIndex = 0; characterIndex < length; characterIndex++)
                {
                    char expected = characterIndex < expectedText.Length ? expectedText[characterIndex] : '∅';
                    char actual = characterIndex < recognized.Length ? recognized[characterIndex] : '∅';
                    if (expected == actual)
                    {
                        characterMatches++;
                    }
                    else
                    {
                        string key = $"{expected}->{actual}";
                        confusion[key] = confusion.GetValueOrDefault(key) + 1;
                    }
                }
            }

            results.Add(new ClockSampleEvaluation(
                sample.Id,
                sample.Label,
                reading.RawRecognizedText,
                reading.Status,
                reading.Confidence,
                exact,
                reading.DiagnosticReason,
                recognition.Status,
                reading.Status,
                recognition.Diagnostics.PreprocessingVariant,
                excludeOwnTemplates,
                recognition.BestCandidate?.Characters ?? [],
                sample.CapturedWithProfile,
                profileId,
                sample.OriginalCandidate,
                sample.OriginalStatus,
                sample.OriginalPreprocessingVariant,
                reading.RawRecognizedText,
                reading.Status));
        }

        ClockEvaluationReport report = new(
            samples.Count,
            correctlyAccepted,
            correctlyRejected,
            falseAccepts,
            falseRejects,
            characterTotal == 0 ? 0 : (double)characterMatches / characterTotal,
            samples.Count == 0 ? 0 : (double)exactMatches / samples.Count,
            confidenceDistribution,
            confusion,
            provenance,
            results,
            evaluationKind,
            results.Count(result => result.VisualStatus != ClockReadingStatus.Valid),
            results.Count(result =>
                result.VisualStatus == ClockReadingStatus.Valid &&
                result.FinalStatus != ClockReadingStatus.Valid),
            results.Where(result =>
                    result.Status == ClockReadingStatus.Valid && !result.ExactMatch)
                .Select(result => result.Id).ToArray(),
            results.Where(result => result.Status != ClockReadingStatus.Valid)
                .Select(result => result.Id).ToArray(),
            compatibility?.Count(item => !item.Accepted) ?? 0,
            compatibility ?? []);
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, reportFileName),
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        return report;
    }

    private static void AddConfidenceBucket(Dictionary<string, int> buckets, double confidence)
    {
        string key = confidence < 0.5
            ? "0.00-0.49"
            : confidence < 0.75
                ? "0.50-0.74"
                : confidence < 0.88
                    ? "0.75-0.87"
                    : "0.88-1.00";
        buckets[key]++;
    }

    private static ClockImage LoadClockImage(
        string path,
        long sequence,
        TimeSpan timestamp) =>
        string.Equals(Path.GetExtension(path), ".bmp", StringComparison.OrdinalIgnoreCase)
            ? LoadBitmap(path, sequence, timestamp)
            : LoadPortableGraymap(path, sequence, timestamp);

    private static ClockImage LoadBitmap(string path, long sequence, TimeSpan timestamp)
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
        if (dibSize < 40 || width <= 0 || storedHeight == 0 || planes != 1 ||
            bitsPerPixel != 32 || compression != 0)
        {
            throw new InvalidDataException(
                $"'{path}' must be an uncompressed 32-bit BMP clock crop.");
        }

        int height = Math.Abs(storedHeight);
        int stride = checked(width * 4);
        byte[] bgra = new byte[checked(stride * height)];
        stream.Position = pixelOffset;
        for (int storedRow = 0; storedRow < height; storedRow++)
        {
            int destinationRow = storedHeight > 0 ? height - 1 - storedRow : storedRow;
            int bytesRead = reader.Read(bgra, destinationRow * stride, stride);
            if (bytesRead != stride)
            {
                throw new InvalidDataException($"'{path}' has truncated pixel data.");
            }
        }

        return new ClockImage(width, height, stride, bgra, sequence, timestamp);
    }

    private static ClockImage LoadPortableGraymap(
        string path,
        long sequence,
        TimeSpan timestamp)
    {
        string[] tokens = File.ReadAllText(path)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4 || tokens[0] != "P2")
        {
            throw new InvalidDataException($"'{path}' must be an ASCII P2 portable graymap.");
        }

        int width = int.Parse(tokens[1], System.Globalization.CultureInfo.InvariantCulture);
        int height = int.Parse(tokens[2], System.Globalization.CultureInfo.InvariantCulture);
        int maximum = int.Parse(tokens[3], System.Globalization.CultureInfo.InvariantCulture);
        if (tokens.Length != 4 + (width * height) || maximum <= 0)
        {
            throw new InvalidDataException($"'{path}' has invalid dimensions or pixels.");
        }

        int stride = width * 4;
        byte[] bgra = new byte[stride * height];
        for (int i = 0; i < width * height; i++)
        {
            byte value = (byte)(int.Parse(
                tokens[4 + i],
                System.Globalization.CultureInfo.InvariantCulture) * 255 / maximum);
            bgra[i * 4] = value;
            bgra[(i * 4) + 1] = value;
            bgra[(i * 4) + 2] = value;
            bgra[(i * 4) + 3] = 255;
        }

        return new ClockImage(width, height, stride, bgra, sequence, timestamp);
    }

    private sealed record EvaluationInput(
        string Id,
        string ImagePath,
        string? Label,
        bool ExpectedAccepted,
        string? SourceDiagnosticBundle,
        string? CapturedWithProfile,
        string? OriginalCandidate,
        string? OriginalStatus,
        string? OriginalPreprocessingVariant);
}
