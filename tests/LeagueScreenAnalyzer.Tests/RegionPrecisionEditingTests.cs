using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Tests;

public sealed class RegionPrecisionEditingTests
{
    private static readonly RegionSourceSize Hd = new(1920, 1080);

    [Fact]
    public void PixelGeometry_RoundTripsIntegerEdges()
    {
        PixelRegion expected = new(1204, 23, 58, 17);

        NormalizedRegion normalized = PixelRegionGeometry.FromPixels(expected, Hd);

        Assert.Equal(expected, PixelRegionGeometry.ToPixels(normalized, Hd));
    }

    [Fact]
    public void ClockRoughDrag_IsStrictlyAtLeastTwoToOneInPixels()
    {
        RegionEditor editor = new(sourceSize: Hd);
        editor.BeginCreate(RegionType.Clock, new NormalizedPoint(0.62, 0.02));
        editor.Update(new NormalizedPoint(0.65, 0.04));
        editor.Commit();

        PixelRegion pixels = PixelRegionGeometry.ToPixels(editor.GetRegion(RegionType.Clock)!, Hd);
        Assert.True(pixels.Width >= pixels.Height * 2);
        Assert.True(editor.Validate(RegionType.Clock)!.IsValid);
    }

    [Fact]
    public void ClockWideDrag_PreservesWideIntent()
    {
        RegionEditor editor = new(sourceSize: Hd);
        editor.BeginCreate(RegionType.Clock, new NormalizedPoint(0.1, 0.1));
        editor.Update(new NormalizedPoint(0.4, 0.15));
        editor.Commit();

        PixelRegion pixels = PixelRegionGeometry.ToPixels(editor.GetRegion(RegionType.Clock)!, Hd);
        Assert.True((double)pixels.Width / pixels.Height > 5);
    }

    [Fact]
    public void ClockExactThreshold_IsAccepted()
    {
        NormalizedRegion region =
            PixelRegionGeometry.FromPixels(new PixelRegion(100, 20, 58, 29), Hd);

        RegionGeometryValidation result =
            new SemanticRegionShapePolicy().ValidateStrict(RegionType.Clock, region, Hd);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.PixelAspectRatio);
    }

    [Fact]
    public void ClockOnePixelRoundingDeviation_IsRuntimeCompatibleAndLegacyNormalized()
    {
        SemanticRegionShapePolicy policy = new();
        NormalizedRegion region =
            PixelRegionGeometry.FromPixels(new PixelRegion(100, 20, 57, 29), Hd);

        Assert.True(policy.Validate(RegionType.Clock, region, Hd).IsValid);
        Assert.False(policy.ValidateStrict(RegionType.Clock, region, Hd).IsValid);
        LegacyRegionNormalization normalized =
            policy.NormalizeLegacy(RegionType.Clock, region, Hd);
        Assert.True(normalized.WasNormalized);
        Assert.Equal(
            new PixelRegion(100, 20, 58, 29),
            PixelRegionGeometry.ToPixels(normalized.Region, Hd));
    }

    [Fact]
    public void ClockMateriallyBelowThreshold_IsRejected()
    {
        NormalizedRegion region =
            PixelRegionGeometry.FromPixels(new PixelRegion(100, 20, 56, 29), Hd);

        Assert.False(new SemanticRegionShapePolicy()
            .Validate(RegionType.Clock, region, Hd).IsValid);
    }

    [Fact]
    public void NumericClockGeometry_RejectsInvalidRatioWithAction()
    {
        RegionEditSession session = ClockSession();

        PixelGeometryUpdate result = session.SetPixelGeometry(1204, 23, 30, 17);

        Assert.False(result.IsValid);
        Assert.Contains("CLOCK", result.Error);
        Assert.Equal(new PixelRegion(1204, 23, 58, 17), session.PixelGeometry);
    }

    [Fact]
    public void NumericMinimapGeometry_UsesOneSizeAndRemainsSquare()
    {
        RegionEditSession session = MinimapSession();

        PixelGeometryUpdate result = session.SetPixelGeometry(1400, 600, 382);

        Assert.True(result.IsValid);
        Assert.Equal(new PixelRegion(1400, 600, 382, 382), session.PixelGeometry);
    }

    [Fact]
    public void MinimapRoughDrag_AndCornerResizeRemainSquare()
    {
        RegionEditor editor = new(sourceSize: Hd);
        editor.BeginCreate(RegionType.Minimap, new NormalizedPoint(0.7, 0.6));
        editor.Update(new NormalizedPoint(0.84, 0.9));
        editor.Commit();
        AssertSquare(editor.GetRegion(RegionType.Minimap)!);

        editor.BeginResize(
            RegionType.Minimap,
            ResizeHandle.BottomRight,
            new NormalizedPoint(0.84, 0.9));
        editor.Update(new NormalizedPoint(0.95, 0.95));
        editor.Commit();
        AssertSquare(editor.GetRegion(RegionType.Minimap)!);
    }

    [Fact]
    public void MoveNudges_OneAndTenPixels_AndClampAtBoundary()
    {
        RegionEditSession session = ClockSession();

        session.MoveByPixels(1, -1);
        Assert.Equal(new PixelRegion(1205, 22, 58, 17), session.PixelGeometry);
        session.MoveByPixels(10, 10);
        Assert.Equal(new PixelRegion(1215, 32, 58, 17), session.PixelGeometry);
        session.MoveByPixels(5000, 5000);
        Assert.Equal(1920, session.PixelGeometry.Right);
        Assert.Equal(1080, session.PixelGeometry.Bottom);
    }

    [Fact]
    public void ClockResizeNudges_OneAndTenPixels_KeepMinimumRatio()
    {
        RegionEditSession session = ClockSession();

        session.ResizeByPixels(1, 0);
        Assert.Equal(59, session.PixelGeometry.Width);
        session.ResizeByPixels(10, 0);
        Assert.Equal(69, session.PixelGeometry.Width);
        session.ResizeByPixels(0, 10);
        Assert.True(session.PixelGeometry.Width >= session.PixelGeometry.Height * 2);
    }

    [Fact]
    public void MinimapResizeNudges_KeepTopLeftAnchorAndSquare()
    {
        RegionEditSession session = MinimapSession();
        PixelRegion original = session.PixelGeometry;

        session.ResizeByPixels(10, 0);

        Assert.Equal(original.X, session.PixelGeometry.X);
        Assert.Equal(original.Y, session.PixelGeometry.Y);
        Assert.Equal(original.Width + 10, session.PixelGeometry.Width);
        Assert.Equal(session.PixelGeometry.Width, session.PixelGeometry.Height);
    }

    [Fact]
    public void ApplyCommitsWorkingGeometry_AndCancelRestoresOriginal()
    {
        RegionEditSession applied = ClockSession();
        applied.MoveByPixels(10, 0);
        Assert.Equal(applied.WorkingRegion, applied.Apply());

        RegionEditSession canceled = ClockSession();
        NormalizedRegion original = canceled.OriginalRegion;
        canceled.MoveByPixels(10, 0);
        Assert.Equal(original, canceled.Cancel());
        Assert.False(canceled.IsDirty);
    }

    [Theory]
    [InlineData(1920, 1080, 300, 300, true)]
    [InlineData(1920, 1080, 300, 301, true)]
    [InlineData(1920, 1080, 300, 302, false)]
    public void MinimapPixelRoundingTolerance_IsAtMostOnePixel(
        int sourceWidth,
        int sourceHeight,
        int width,
        int height,
        bool valid)
    {
        RegionSourceSize source = new(sourceWidth, sourceHeight);
        NormalizedRegion region = PixelRegionGeometry.FromPixels(
            new PixelRegion(100, 100, width, height),
            source);

        Assert.Equal(
            valid,
            new SemanticRegionShapePolicy().Validate(RegionType.Minimap, region, source).IsValid);
    }

    private static RegionEditSession ClockSession() =>
        new(
            RegionType.Clock,
            PixelRegionGeometry.FromPixels(new PixelRegion(1204, 23, 58, 17), Hd),
            Hd);

    private static RegionEditSession MinimapSession() =>
        new(
            RegionType.Minimap,
            PixelRegionGeometry.FromPixels(new PixelRegion(1400, 600, 300, 300), Hd),
            Hd);

    private static void AssertSquare(NormalizedRegion region)
    {
        PixelRegion pixels = PixelRegionGeometry.ToPixels(region, Hd);
        Assert.Equal(pixels.Width, pixels.Height);
    }
}
