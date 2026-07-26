using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockRecognitionTests
{
    [Theory]
    [InlineData("0:00", 4)]
    [InlineData("12:43", 5)]
    public async Task Recognizer_RecognizesDeterministicTemplateFixture(string text, int segmentCount)
    {
        ClockImage image = ClockTestImages.Render(text);
        ClockRecognitionResult result = await new ConstrainedClockImageRecognizer().RecognizeAsync(
            image,
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id));

        Assert.Equal(ClockReadingStatus.Valid, result.Status);
        Assert.Equal(text, result.BestCandidate?.Text);
        Assert.Equal(image.Width, result.Diagnostics.NormalizedWidth);
        Assert.Equal(image.Height, result.Diagnostics.NormalizedHeight);
        Assert.Equal(segmentCount, result.Diagnostics.Segments.Count);
        Assert.True(result.Confidence >= 0.88);
    }

    [Fact]
    public async Task Recognizer_HandlesSeparatorAndOrdersAmbiguousCandidates()
    {
        ClockRecognitionResult result = await new ConstrainedClockImageRecognizer().RecognizeAsync(
            ClockTestImages.Render("8:08"),
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id));

        Assert.Equal(':', result.BestCandidate!.Characters[1].Character);
        Assert.True(result.Candidates.Count > 1);
        Assert.True(result.Candidates[0].Confidence >= result.Candidates[1].Confidence);
    }

    [Fact]
    public async Task Recognizer_RejectsNoCharacterImage()
    {
        ClockRecognitionResult result = await new ConstrainedClockImageRecognizer().RecognizeAsync(
            ClockTestImages.Solid(14, 7, 0),
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id));

        Assert.Equal(ClockReadingStatus.NotVisible, result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Recognizer_RejectsLowContrastImage()
    {
        ClockImage image = ClockTestImages.Render("1:23", foreground: 105, background: 100);
        ClockRecognitionResult result = await new ConstrainedClockImageRecognizer().RecognizeAsync(
            image,
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id));

        Assert.Equal(ClockReadingStatus.NotVisible, result.Status);
    }

    [Fact]
    public async Task Recognizer_RespectsForegroundPolarity()
    {
        ClockRecognitionProfile profile = BuiltInClockProfiles
            .Get(BuiltInClockProfiles.LeagueReplayV1Id) with
        {
            ForegroundPolarity = ClockForegroundPolarity.DarkOnLight
        };
        ClockRecognitionResult result = await new ConstrainedClockImageRecognizer().RecognizeAsync(
            ClockTestImages.Render("1:23", foreground: 0, background: 255),
            profile);

        Assert.Equal("1:23", result.BestCandidate?.Text);
    }
}

internal static class ClockTestImages
{
    private static readonly IReadOnlyDictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['0'] = ["11111", "10001", "10001", "10001", "10001", "10001", "11111"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["11111", "00001", "00001", "11111", "10000", "10000", "11111"],
        ['3'] = ["11111", "00001", "00001", "11111", "00001", "00001", "11111"],
        ['4'] = ["10001", "10001", "10001", "11111", "00001", "00001", "00001"],
        ['5'] = ["11111", "10000", "10000", "11111", "00001", "00001", "11111"],
        ['6'] = ["11111", "10000", "10000", "11111", "10001", "10001", "11111"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["11111", "10001", "10001", "11111", "10001", "10001", "11111"],
        ['9'] = ["11111", "10001", "10001", "11111", "00001", "00001", "11111"],
        [':'] = ["0", "0", "1", "0", "1", "0", "0"]
    };

    public static ClockImage Render(
        string text,
        byte foreground = 255,
        byte background = 0,
        long sequence = 0,
        double timestampSeconds = 0)
    {
        int width = text.Sum(character => Glyphs[character][0].Length) + text.Length - 1;
        int stride = width * 4;
        byte[] pixels = new byte[stride * 7];
        for (int y = 0; y < 7; y++)
        {
            int x = 0;
            foreach (char character in text)
            {
                string row = Glyphs[character][y];
                foreach (char bit in row)
                {
                    SetPixel(pixels, stride, x++, y, bit == '1' ? foreground : background);
                }

                if (x < width)
                {
                    SetPixel(pixels, stride, x++, y, background);
                }
            }
        }

        return new ClockImage(
            width,
            7,
            stride,
            pixels,
            sequence,
            TimeSpan.FromSeconds(timestampSeconds));
    }

    public static ClockImage Solid(int width, int height, byte value)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SetPixel(pixels, stride, x, y, value);
            }
        }

        return new ClockImage(width, height, stride, pixels, 0, TimeSpan.Zero);
    }

    private static void SetPixel(byte[] pixels, int stride, int x, int y, byte value)
    {
        int index = (y * stride) + (x * 4);
        pixels[index] = value;
        pixels[index + 1] = value;
        pixels[index + 2] = value;
        pixels[index + 3] = 255;
    }
}
