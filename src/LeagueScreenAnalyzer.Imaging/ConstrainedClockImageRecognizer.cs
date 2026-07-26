using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Imaging;

public sealed class ConstrainedClockImageRecognizer : IClockImageRecognizer
{
    private const int NormalizedCharacterWidth = 5;
    private const int NormalizedCharacterHeight = 7;

    public ValueTask<ClockRecognitionResult> RecognizeAsync(
        ClockImage image,
        ClockRecognitionProfile profile,
        CancellationToken cancellationToken = default) =>
        RecognizeAsync(image, profile, new ClockRecognitionOptions(), cancellationToken);

    public ValueTask<ClockRecognitionResult> RecognizeAsync(
        ClockImage image,
        ClockRecognitionProfile profile,
        ClockRecognitionOptions options,
        CancellationToken cancellationToken = default)
    {
        image.Validate();
        profile.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        RegionGeometryValidation geometry = new SemanticRegionShapePolicy().Validate(
            RegionType.Clock,
            new NormalizedRegion(0, 0, 1, 1),
            new RegionSourceSize(image.Width, image.Height));
        if (!geometry.IsValid)
        {
            return ValueTask.FromResult(new ClockRecognitionResult(
                [],
                ClockReadingStatus.NotConfigured,
                0,
                geometry.Error,
                new ClockRecognitionDiagnostics(
                    image.Width,
                    image.Height,
                    [],
                    [],
                    "Geometry",
                    geometry.Error)));
        }

        byte[] luminance = ToLuminance(image);
        (byte minimum, byte maximum) = FindRange(luminance);
        if (maximum - minimum < 18)
        {
            return ValueTask.FromResult(Empty(
                image,
                luminance,
                ClockReadingStatus.NotVisible,
                "Clock crop has insufficient luminance range."));
        }

        byte threshold = profile.ThresholdStrategy == ClockThresholdStrategy.Otsu
            ? CalculateOtsuThreshold(luminance)
            : profile.FixedThreshold;
        byte[] foreground = Threshold(luminance, threshold, profile.ForegroundPolarity);
        IReadOnlyList<ClockSegment> segments = Segment(foreground, image.Width, image.Height);
        ClockRecognitionDiagnostics diagnostics = new(
            image.Width,
            image.Height,
            foreground,
            segments,
            $"{profile.ThresholdStrategy}/{profile.ForegroundPolarity}");

        if (segments.Count == 0)
        {
            return ValueTask.FromResult(new ClockRecognitionResult(
                [],
                ClockReadingStatus.NotVisible,
                0,
                "No foreground characters were found.",
                diagnostics));
        }

        IReadOnlyList<ClockGlyphTemplate>? realTemplates = null;
        ClockTemplateManifest? templateManifest = null;
        if (ClockTemplateProfileLoader.TryFindProfileDirectory(
                profile.Id, out string? profileDirectory))
        {
            templateManifest = ClockTemplateProfileLoader.LoadManifest(profileDirectory!);
            realTemplates = ClockTemplateProfileLoader.LoadTemplates(profileDirectory!)
                .Where(template => !string.Equals(
                    template.Provenance.SourceDiagnosticBundle,
                    options.ExcludedSourceDiagnosticBundle,
                    StringComparison.Ordinal))
                .ToArray();
        }
        else if (!string.Equals(
                     profile.Id,
                     BuiltInClockProfiles.LeagueReplayV1Id,
                     StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Clock profile '{profile.Id}' requires installed template assets, but its validated manifest is unavailable.");
        }

        List<List<ClockCharacterCandidate>> characterCandidates = [];
        for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            ClockSegment segment = segments[segmentIndex];
            if (realTemplates is not null && templateManifest is not null)
            {
                bool separatorPosition = IsSeparatorPosition(segmentIndex, segments.Count);
                List<ClockCharacterCandidate> realMatches = MatchRealTemplates(
                    segment,
                    realTemplates,
                    templateManifest,
                    separatorPosition);
                if (realMatches.Count == 0)
                {
                    return ValueTask.FromResult(new ClockRecognitionResult(
                        [],
                        ClockReadingStatus.LowConfidence,
                        0,
                        separatorPosition
                            ? "No independent real separator template supports this segment."
                            : "No independent real digit template supports this segment.",
                        diagnostics));
                }

                characterCandidates.Add(realMatches);
                continue;
            }

            if (LooksLikeSeparator(segment))
            {
                characterCandidates.Add([new ClockCharacterCandidate(':', 0.99)]);
                continue;
            }

            bool[] normalized = ResizeToMask(segment.Pixels, segment.Width, segment.Height);
            List<ClockCharacterCandidate> matches = SevenSegmentTemplates.Digits
                .Select(digit => new ClockCharacterCandidate(
                    digit,
                    Similarity(normalized, SevenSegmentTemplates.Get(digit))))
                .OrderByDescending(candidate => candidate.Confidence)
                .Take(2)
                .ToList();
            characterCandidates.Add(matches);
        }

        List<ClockCandidate> candidates = BuildCandidates(characterCandidates, profile);
        if (candidates.Count == 0)
        {
            return ValueTask.FromResult(new ClockRecognitionResult(
                [],
                ClockReadingStatus.Malformed,
                0,
                "Localized characters did not form a valid clock.",
                diagnostics));
        }

        ClockCandidate best = candidates[0];
        ClockReadingStatus status = best.ParsedGameTime is null
            ? ClockReadingStatus.Malformed
            : best.Confidence < profile.MinimumRecognitionConfidence
                ? ClockReadingStatus.LowConfidence
                : ClockReadingStatus.Valid;
        return ValueTask.FromResult(new ClockRecognitionResult(
            candidates,
            status,
            best.Confidence,
            status switch
            {
                ClockReadingStatus.Valid => null,
                ClockReadingStatus.Malformed => best.Diagnostic,
                _ => "Best image candidate is below the profile confidence threshold."
            },
            diagnostics));
    }

    private static bool IsSeparatorPosition(int index, int count) =>
        (count == 4 && index == 1) || (count == 5 && index == 2);

    private static List<ClockCharacterCandidate> MatchRealTemplates(
        ClockSegment segment,
        IReadOnlyList<ClockGlyphTemplate> templates,
        ClockTemplateManifest manifest,
        bool separatorPosition)
    {
        bool[] normalized = ClockTemplateMatcher.Normalize(
            segment.Pixels,
            segment.Width,
            segment.Height,
            manifest.TemplateWidth,
            manifest.TemplateHeight);
        List<(char Glyph, double Score, string Source)> scores = templates
            .Where(template => separatorPosition ? template.Glyph == ':' : char.IsAsciiDigit(template.Glyph))
            .GroupBy(template => template.Glyph)
            .Select(group =>
            {
                var best = group
                    .Select(template => new
                    {
                        Template = template,
                        Score = ClockTemplateMatcher.SimilarityWithTranslation(
                            normalized,
                            template.Pixels,
                            manifest.TemplateWidth,
                            manifest.TemplateHeight)
                    })
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Template.TemplateId, StringComparer.Ordinal)
                    .First();
                return (group.Key, best.Score, best.Template.Provenance.SourceDiagnosticBundle);
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Key)
            .Select(item => (item.Key, item.Score, item.SourceDiagnosticBundle))
            .ToList();
        if (scores.Count == 0)
        {
            return [];
        }

        double second = scores.Count > 1 ? scores[1].Score : 0;
        return scores.Take(separatorPosition ? 1 : 2)
            .Select((item, index) => new ClockCharacterCandidate(
                item.Glyph,
                item.Score,
                index == 0 ? Math.Max(0, item.Score - second) : 0,
                item.Source))
            .ToList();
    }

    private static ClockRecognitionResult Empty(
        ClockImage image,
        byte[] pixels,
        ClockReadingStatus status,
        string reason) =>
        new([], status, 0, reason, new ClockRecognitionDiagnostics(
            image.Width, image.Height, pixels, [], "luminance-only", reason));

    private static byte[] ToLuminance(ClockImage image)
    {
        byte[] output = new byte[image.Width * image.Height];
        ReadOnlySpan<byte> source = image.BgraPixels.Span;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int index = (y * image.Stride) + (x * 4);
                output[(y * image.Width) + x] = (byte)(
                    ((source[index + 2] * 54) + (source[index + 1] * 183) + (source[index] * 19)) >> 8);
            }
        }

        return output;
    }

    private static (byte Minimum, byte Maximum) FindRange(byte[] pixels) =>
        (pixels.Min(), pixels.Max());

    private static byte CalculateOtsuThreshold(byte[] pixels)
    {
        int[] histogram = new int[256];
        foreach (byte pixel in pixels)
        {
            histogram[pixel]++;
        }

        long totalSum = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            totalSum += (long)i * histogram[i];
        }

        long backgroundSum = 0;
        int backgroundWeight = 0;
        double maximumVariance = -1;
        byte selected = 127;
        for (int threshold = 0; threshold < 256; threshold++)
        {
            backgroundWeight += histogram[threshold];
            if (backgroundWeight == 0)
            {
                continue;
            }

            int foregroundWeight = pixels.Length - backgroundWeight;
            if (foregroundWeight == 0)
            {
                break;
            }

            backgroundSum += (long)threshold * histogram[threshold];
            double backgroundMean = (double)backgroundSum / backgroundWeight;
            double foregroundMean = (double)(totalSum - backgroundSum) / foregroundWeight;
            double variance = (double)backgroundWeight * foregroundWeight *
                              (backgroundMean - foregroundMean) * (backgroundMean - foregroundMean);
            if (variance > maximumVariance)
            {
                maximumVariance = variance;
                selected = (byte)threshold;
            }
        }

        return selected;
    }

    private static byte[] Threshold(
        byte[] pixels,
        byte threshold,
        ClockForegroundPolarity polarity)
    {
        byte[] output = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            bool foreground = polarity == ClockForegroundPolarity.LightOnDark
                ? pixels[i] > threshold
                : pixels[i] <= threshold;
            output[i] = foreground ? byte.MaxValue : byte.MinValue;
        }

        return output;
    }

    private static IReadOnlyList<ClockSegment> Segment(byte[] pixels, int width, int height)
    {
        List<ClockSegment> segments = [];
        int x = 0;
        while (x < width)
        {
            while (x < width && !ColumnHasForeground(pixels, width, height, x))
            {
                x++;
            }

            if (x == width)
            {
                break;
            }

            int left = x;
            while (x < width && ColumnHasForeground(pixels, width, height, x))
            {
                x++;
            }

            int right = x - 1;
            int top = height;
            int bottom = -1;
            for (int px = left; px <= right; px++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (pixels[(y * width) + px] != 0)
                    {
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            if (bottom >= top)
            {
                int segmentWidth = right - left + 1;
                int segmentHeight = bottom - top + 1;
                byte[] segmentPixels = new byte[segmentWidth * segmentHeight];
                for (int y = 0; y < segmentHeight; y++)
                {
                    Array.Copy(pixels, ((top + y) * width) + left, segmentPixels, y * segmentWidth, segmentWidth);
                }

                segments.Add(new ClockSegment(left, top, segmentWidth, segmentHeight, segmentPixels));
            }
        }

        return segments;
    }

    private static bool ColumnHasForeground(byte[] pixels, int width, int height, int x)
    {
        for (int y = 0; y < height; y++)
        {
            if (pixels[(y * width) + x] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeSeparator(ClockSegment segment) =>
        segment.Width <= Math.Max(2, segment.Height / 3) &&
        segment.Pixels.Count(pixel => pixel != 0) <= Math.Max(4, segment.Height);

    private static bool[] ResizeToMask(byte[] pixels, int width, int height)
    {
        bool[] output = new bool[NormalizedCharacterWidth * NormalizedCharacterHeight];
        for (int y = 0; y < NormalizedCharacterHeight; y++)
        {
            int sourceTop = y * height / NormalizedCharacterHeight;
            int sourceBottom = Math.Max(sourceTop + 1, (y + 1) * height / NormalizedCharacterHeight);
            for (int x = 0; x < NormalizedCharacterWidth; x++)
            {
                int sourceLeft = x * width / NormalizedCharacterWidth;
                int sourceRight = Math.Max(sourceLeft + 1, (x + 1) * width / NormalizedCharacterWidth);
                int foreground = 0;
                int total = 0;
                for (int sy = sourceTop; sy < Math.Min(height, sourceBottom); sy++)
                {
                    for (int sx = sourceLeft; sx < Math.Min(width, sourceRight); sx++)
                    {
                        total++;
                        if (pixels[(sy * width) + sx] != 0)
                        {
                            foreground++;
                        }
                    }
                }

                output[(y * NormalizedCharacterWidth) + x] = foreground * 2 >= total;
            }
        }

        return output;
    }

    private static double Similarity(bool[] left, bool[] right)
    {
        int equal = 0;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] == right[i])
            {
                equal++;
            }
        }

        return (double)equal / left.Length;
    }

    private static List<ClockCandidate> BuildCandidates(
        IReadOnlyList<List<ClockCharacterCandidate>> characters,
        ClockRecognitionProfile profile)
    {
        if (characters.Count < profile.MinimumCharacterCount ||
            characters.Count > profile.MaximumCharacterCount)
        {
            return [];
        }

        List<ClockCandidate> candidates = [];
        Build(0, [], 1);
        return candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .Take(8)
            .ToList();

        void Build(int index, List<ClockCharacterCandidate> selected, double confidenceProduct)
        {
            if (index == characters.Count)
            {
                string text = new(selected.Select(item => item.Character).ToArray());
                double confidence = Math.Pow(confidenceProduct, 1d / selected.Count);
                if (selected.Any(item => item.Margin > 0))
                {
                    double absoluteFloor = selected.Min(item => item.Confidence);
                    double marginFloor = selected
                        .Where(item => item.Character != ':')
                        .Min(item => item.Margin);
                    double evidenceFloor =
                        (0.85 * absoluteFloor) +
                        (0.15 * Math.Clamp(marginFloor / 0.15, 0, 1));
                    confidence = Math.Min(confidence, evidenceFloor);
                }
                TimeSpan? parsed = ClockParser.TryParse(text, profile.MaximumGameTime, out TimeSpan gameTime, out string? reason)
                    ? gameTime
                    : null;
                candidates.Add(new ClockCandidate(text, parsed, confidence, selected.ToArray(), reason));
                return;
            }

            foreach (ClockCharacterCandidate candidate in characters[index])
            {
                selected.Add(candidate);
                Build(index + 1, selected, confidenceProduct * candidate.Confidence);
                selected.RemoveAt(selected.Count - 1);
            }
        }
    }
}

public sealed record ClockRecognitionOptions(string? ExcludedSourceDiagnosticBundle = null);
