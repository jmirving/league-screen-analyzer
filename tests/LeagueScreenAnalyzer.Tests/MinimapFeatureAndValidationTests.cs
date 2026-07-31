using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class MinimapFeatureAndValidationTests
{
    [Fact]
    public void Extract_ReportsDeterministicUniformBlackStatistics()
    {
        using MapImage image = Image(100, 100, (_, _) => 0);
        MinimapFeatureExtractor extractor = new();

        MapFeatureValues first = extractor.Extract(image);
        MapFeatureValues second = extractor.Extract(image);

        Assert.Equal(first, second);
        Assert.Equal(0, first.MeanLuminance);
        Assert.Equal(0, first.LuminanceVariance);
        Assert.Equal(1, first.NearBlackPercentage);
        Assert.Equal(0, first.EdgeDensity);
        Assert.Equal(1, first.BorderConsistency);
        Assert.Equal(1, first.CornerConsistency);
        Assert.Equal(1, first.AspectRatio);
    }

    [Fact]
    public void Extract_ReportsStructuredEdgesAndLuminanceSpread()
    {
        using MapImage image = Image(128, 128, (x, y) =>
            (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 30 : 210));

        MapFeatureValues features = new MinimapFeatureExtractor().Extract(image);

        Assert.True(features.LuminanceVariance > 1000);
        Assert.Equal((byte)30, features.MinimumLuminance);
        Assert.Equal((byte)210, features.MaximumLuminance);
        Assert.InRange(features.EdgeDensity, 0.10, 0.20);
    }

    [Fact]
    public async Task Validate_RejectsBlackAndIncompatibleGeometry()
    {
        StructuralMinimapValidator validator = new(PermissiveProfile());
        using MapImage black = Image(128, 128, (_, _) => 0);
        using MapImage wide = Image(200, 100, (x, y) => (byte)((x + y) % 255));

        MapValidationResult blackResult = await validator.ValidateAsync(black);
        MapValidationResult wideResult = await validator.ValidateAsync(wide);

        Assert.Equal(MapFrameStatus.Obscured, blackResult.Status);
        Assert.Equal(MapFrameStatus.IncompatibleGeometry, wideResult.Status);
    }

    [Fact]
    public async Task Validate_AcceptsStructuredCropAndRejectsLowInformation()
    {
        StructuralMinimapValidator validator = new(PermissiveProfile());
        using MapImage structured = Image(128, 128, (x, y) =>
            (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 30 : 210));
        using MapImage flat = Image(128, 128, (x, y) => (byte)(80 + ((x + y) % 2)));

        MapValidationResult accepted = await validator.ValidateAsync(structured);
        MapValidationResult rejected = await validator.ValidateAsync(flat);

        Assert.Equal(MapFrameStatus.Valid, accepted.Status);
        Assert.NotNull(accepted.Features);
        Assert.Equal(MapFrameStatus.LowInformation, rejected.Status);
    }

    [Fact]
    public async Task Profile_RoundTripsAndMalformedProfileFails()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "profile.json");
        await MinimapProfileSerializer.SaveAsync(PermissiveProfile(), path);

        MinimapValidationProfile loaded = await MinimapProfileSerializer.LoadAsync(path);
        Assert.Equal("test-minimap-v1", loaded.Id);

        await File.WriteAllTextAsync(path, """{"id":"broken"}""");
        await Assert.ThrowsAnyAsync<Exception>(() => MinimapProfileSerializer.LoadAsync(path));
    }

    private static MinimapValidationProfile PermissiveProfile() => new(
        "test-minimap-v1",
        "Test minimap",
        1,
        SessionMode.ReplayContinuous,
        1,
        0.05,
        64,
        64,
        64,
        64,
        50,
        20,
        0.8,
        0.8,
        0.05,
        0.3,
        0,
        0,
        0.2,
        "Deterministic test profile.",
        true);

    internal static MapImage Image(
        int width,
        int height,
        Func<int, int, byte> luminance,
        long sequence = 1,
        TimeSpan? timestamp = null)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte value = luminance(x, y);
            int offset = ((y * width) + x) * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }

        return new MapImage(
            width,
            height,
            width * 4,
            pixels,
            sequence,
            timestamp ?? TimeSpan.FromMilliseconds(sequence * 100));
    }
}
