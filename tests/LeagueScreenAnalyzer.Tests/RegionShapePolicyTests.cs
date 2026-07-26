using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Tests;

public sealed class RegionShapePolicyTests
{
    private static readonly RegionSourceSize Hd = new(1920, 1080);

    [Fact]
    public void MinimapCreate_IsSquareInSourcePixels()
    {
        RegionEditor editor = new(sourceSize: Hd);
        editor.BeginCreate(RegionType.Minimap, new NormalizedPoint(0.7, 0.6));
        editor.Update(new NormalizedPoint(0.88, 0.82));
        editor.Commit();

        AssertSquare(editor.GetRegion(RegionType.Minimap)!);
    }

    [Theory]
    [InlineData(ResizeHandle.TopLeft)]
    [InlineData(ResizeHandle.TopRight)]
    [InlineData(ResizeHandle.BottomRight)]
    [InlineData(ResizeHandle.BottomLeft)]
    [InlineData(ResizeHandle.Top)]
    [InlineData(ResizeHandle.Right)]
    [InlineData(ResizeHandle.Bottom)]
    [InlineData(ResizeHandle.Left)]
    public void MinimapResize_AllHandlesPreserveSquare(ResizeHandle handle)
    {
        RegionEditor editor = SquareEditor();
        editor.BeginResize(RegionType.Minimap, handle, new NormalizedPoint(0.75, 0.65));
        editor.Update(handle switch
        {
            ResizeHandle.TopLeft => new NormalizedPoint(0.55, 0.45),
            ResizeHandle.Top => new NormalizedPoint(0.75, 0.45),
            ResizeHandle.TopRight => new NormalizedPoint(0.95, 0.45),
            ResizeHandle.Right => new NormalizedPoint(0.95, 0.65),
            ResizeHandle.BottomRight => new NormalizedPoint(0.95, 0.9),
            ResizeHandle.Bottom => new NormalizedPoint(0.75, 0.9),
            ResizeHandle.BottomLeft => new NormalizedPoint(0.55, 0.9),
            ResizeHandle.Left => new NormalizedPoint(0.55, 0.65),
            _ => throw new InvalidOperationException()
        });
        editor.Commit();

        AssertSquare(editor.GetRegion(RegionType.Minimap)!);
    }

    [Fact]
    public void MinimapMove_PreservesSizeAndClampsAtBoundaries()
    {
        RegionEditor editor = SquareEditor();
        NormalizedRegion original = editor.GetRegion(RegionType.Minimap)!;
        editor.BeginMove(RegionType.Minimap, new NormalizedPoint(0.75, 0.65));
        editor.Update(new NormalizedPoint(1, 1));
        editor.Commit();

        NormalizedRegion moved = editor.GetRegion(RegionType.Minimap)!;
        Assert.Equal(original.Width, moved.Width, 10);
        Assert.Equal(original.Height, moved.Height, 10);
        Assert.Equal(1, moved.X + moved.Width, 10);
        Assert.Equal(1, moved.Y + moved.Height, 10);
        AssertSquare(moved);
    }

    [Fact]
    public void MinimapMinimumSizeAndBoundaryClamp_PreserveSquare()
    {
        RegionEditor editor = new(0.02, 0.02, sourceSize: Hd);
        editor.BeginCreate(RegionType.Minimap, new NormalizedPoint(0.995, 0.995));
        editor.Update(new NormalizedPoint(1, 1));
        editor.Commit();

        NormalizedRegion region = editor.GetRegion(RegionType.Minimap)!;
        Assert.True(region.Width > 0);
        Assert.True(region.Height > 0);
        Assert.True(region.X + region.Width <= 1);
        Assert.True(region.Y + region.Height <= 1);
        AssertSquare(region);
    }

    [Fact]
    public void SquareNormalizedRegion_ConvertsToPixelsWithinOnePixel()
    {
        NormalizedRegion region = new(0.7313, 0.6127, 200d / 1920, 200d / 1080);
        (int width, int height) = PixelSize(region, 1920, 1080);

        Assert.InRange(Math.Abs(width - height), 0, 1);
    }

    [Fact]
    public void LegacyMinimap_SlightDeviationNormalizesAndMaterialDeviationWarns()
    {
        SemanticRegionShapePolicy policy = new();
        NormalizedRegion slight = new(0.7, 0.6, 311d / 1920, 305d / 1080);
        LegacyRegionNormalization normalized =
            policy.NormalizeLegacy(RegionType.Minimap, slight, Hd);
        Assert.True(normalized.WasNormalized);
        Assert.NotNull(normalized.Warning);
        AssertSquare(normalized.Region);

        NormalizedRegion material = new(0.7, 0.6, 400d / 1920, 250d / 1080);
        LegacyRegionNormalization retained =
            policy.NormalizeLegacy(RegionType.Minimap, material, Hd);
        Assert.False(retained.WasNormalized);
        Assert.Equal(material, retained.Region);
        Assert.Contains("materially", retained.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0.4, 0.1, true)]
    [InlineData(0.2, 0.2, false)]
    [InlineData(0.1, 0.3, false)]
    public void ClockValidation_RequiresWideHorizontalGeometry(
        double width,
        double height,
        bool expectedValid)
    {
        RegionGeometryValidation result = new SemanticRegionShapePolicy().Validate(
            RegionType.Clock,
            new NormalizedRegion(0.1, 0.1, width, height),
            RegionSourceSize.Unit);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void ClockCreateAndResize_EnforceMinimumRatio()
    {
        RegionEditor editor = new();
        editor.BeginCreate(RegionType.Clock, new NormalizedPoint(0.2, 0.2));
        editor.Update(new NormalizedPoint(0.4, 0.4));
        editor.Commit();
        Assert.True(editor.Validate(RegionType.Clock)!.IsValid);

        editor.BeginResize(
            RegionType.Clock,
            ResizeHandle.Bottom,
            new NormalizedPoint(0.3, 0.4));
        editor.Update(new NormalizedPoint(0.3, 0.9));
        editor.Commit();
        Assert.True(editor.Validate(RegionType.Clock)!.IsValid);
    }

    private static RegionEditor SquareEditor()
    {
        RegionEditor editor = new(sourceSize: Hd);
        editor.SetRegion(
            RegionType.Minimap,
            new NormalizedRegion(0.7, 0.55, 180d / 1920, 180d / 1080));
        return editor;
    }

    private static void AssertSquare(NormalizedRegion region)
    {
        double ratio = SemanticRegionShapePolicy.PixelAspectRatio(region, Hd);
        Assert.Equal(1, ratio, 8);
    }

    private static (int Width, int Height) PixelSize(
        NormalizedRegion region,
        int width,
        int height)
    {
        int left = (int)Math.Floor(region.X * width);
        int top = (int)Math.Floor(region.Y * height);
        int right = (int)Math.Ceiling((region.X + region.Width) * width);
        int bottom = (int)Math.Ceiling((region.Y + region.Height) * height);
        return (right - left, bottom - top);
    }
}
