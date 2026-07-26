using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockGeometryValidationTests
{
    [Fact]
    public async Task GameClockReader_DoesNotInvokeRecognizerForSquareCrop()
    {
        CountingRecognizer recognizer = new();
        GameClockReader reader = new(
            recognizer,
            new ClockTemporalValidator(),
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id));
        PixelPayload payload = new(100, 100);
        RegionFrame frame = new(
            RegionType.Clock,
            1,
            TimeSpan.Zero,
            100,
            100,
            payload);

        ClockReading result = await reader.ReadAsync(frame);

        Assert.Equal(ClockReadingStatus.NotConfigured, result.Status);
        Assert.Contains("wide horizontal", result.DiagnosticReason);
        Assert.Equal(0, recognizer.CallCount);
    }

    private sealed class CountingRecognizer : IClockImageRecognizer
    {
        public int CallCount { get; private set; }

        public ValueTask<ClockRecognitionResult> RecognizeAsync(
            ClockImage image,
            ClockRecognitionProfile profile,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Recognizer should not be invoked.");
        }
    }

    private sealed class PixelPayload : IClockImagePayload
    {
        public PixelPayload(int width, int height)
        {
            Stride = width * 4;
            BgraPixels = new byte[Stride * height];
        }

        public ReadOnlyMemory<byte> BgraPixels { get; }
        public int Stride { get; }
    }
}
