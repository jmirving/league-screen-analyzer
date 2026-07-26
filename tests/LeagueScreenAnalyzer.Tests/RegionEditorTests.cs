using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;

namespace LeagueScreenAnalyzer.Tests;

public sealed class RegionEditorTests
{
    [Fact]
    public void CreateRegion_NormalizesDragDirectionAndCommits()
    {
        RegionEditor editor = new();
        editor.BeginCreate(RegionType.Clock, new NormalizedPoint(0.6, 0.5));
        editor.Update(new NormalizedPoint(0.2, 0.1));

        RegionEditResult result = editor.Commit();

        Assert.NotNull(result.After);
        Assert.Equal(0.2, result.After.X, 8);
        Assert.Equal(0.1, result.After.Y, 8);
        Assert.Equal(0.8, result.After.Width, 8);
        Assert.Equal(0.4, result.After.Height, 8);
        Assert.True(editor.Validate(RegionType.Clock)!.IsValid);
        Assert.Equal(RegionEditOperation.Create, result.Operation);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public void MoveRegion_ClampsToEveryBoundary()
    {
        RegionEditor editor = Configured();
        editor.BeginMove(RegionType.Clock, new NormalizedPoint(0.3, 0.3));
        editor.Update(new NormalizedPoint(0, 0));
        editor.Commit();
        Assert.Equal(new NormalizedRegion(0, 0, 0.2, 0.2), editor.GetRegion(RegionType.Clock));

        editor.BeginMove(RegionType.Clock, new NormalizedPoint(0, 0));
        editor.Update(new NormalizedPoint(1, 1));
        editor.Commit();
        Assert.Equal(new NormalizedRegion(0.8, 0.8, 0.2, 0.2), editor.GetRegion(RegionType.Clock));
    }

    [Theory]
    [InlineData(ResizeHandle.TopLeft)]
    [InlineData(ResizeHandle.Top)]
    [InlineData(ResizeHandle.TopRight)]
    [InlineData(ResizeHandle.Right)]
    [InlineData(ResizeHandle.BottomRight)]
    [InlineData(ResizeHandle.Bottom)]
    [InlineData(ResizeHandle.BottomLeft)]
    [InlineData(ResizeHandle.Left)]
    public void Resize_AllHandles_PreserveClockSemanticGeometry(ResizeHandle handle)
    {
        RegionEditor editor = new();
        editor.SetRegion(RegionType.Clock, new NormalizedRegion(0.2, 0.2, 0.4, 0.1));
        NormalizedPoint target = handle switch
        {
            ResizeHandle.TopLeft => new(0.1, 0.1),
            ResizeHandle.Top => new(0.4, 0.05),
            ResizeHandle.TopRight => new(0.7, 0.1),
            ResizeHandle.Right => new(0.7, 0.25),
            ResizeHandle.BottomRight => new(0.7, 0.4),
            ResizeHandle.Bottom => new(0.4, 0.4),
            ResizeHandle.BottomLeft => new(0.1, 0.4),
            ResizeHandle.Left => new(0.1, 0.25),
            _ => throw new InvalidOperationException()
        };
        editor.BeginResize(RegionType.Clock, handle, new NormalizedPoint(0.2, 0.2));
        editor.Update(target);
        editor.Commit();

        NormalizedRegion region = editor.GetRegion(RegionType.Clock)!;
        Assert.InRange(region.X, 0, 1);
        Assert.InRange(region.Y, 0, 1);
        Assert.InRange(region.X + region.Width, 0, 1);
        Assert.InRange(region.Y + region.Height, 0, 1);
        Assert.True(editor.Validate(RegionType.Clock)!.IsValid);
    }

    [Theory]
    [InlineData(ResizeHandle.TopLeft, 0, 0)]
    [InlineData(ResizeHandle.TopRight, 1, 0)]
    [InlineData(ResizeHandle.BottomRight, 1, 1)]
    [InlineData(ResizeHandle.BottomLeft, 0, 1)]
    public void Resize_ClampsToSourceBoundaries(
        ResizeHandle handle,
        double targetX,
        double targetY)
    {
        RegionEditor editor = Configured();
        editor.BeginResize(RegionType.Clock, handle, new NormalizedPoint(0.2, 0.2));
        editor.Update(new NormalizedPoint(targetX, targetY));
        editor.Commit();

        NormalizedRegion region = editor.GetRegion(RegionType.Clock)!;
        Assert.True(region.X >= 0);
        Assert.True(region.Y >= 0);
        Assert.True(region.X + region.Width <= 1);
        Assert.True(region.Y + region.Height <= 1);
    }

    [Fact]
    public void Resize_EnforcesMinimumAndDoesNotInvert()
    {
        RegionEditor editor = new(0.05, 0.04);
        editor.SetRegion(RegionType.Clock, new NormalizedRegion(0.2, 0.2, 0.2, 0.05));
        editor.BeginResize(RegionType.Clock, ResizeHandle.Left, new NormalizedPoint(0.2, 0.3));
        editor.Update(new NormalizedPoint(0.9, 0.3));
        editor.Commit();

        NormalizedRegion region = editor.GetRegion(RegionType.Clock)!;
        Assert.True(region.Width >= 0.05);
        Assert.True(region.X + region.Width <= 1);
        Assert.True(editor.Validate(RegionType.Clock)!.IsValid);
    }

    [Fact]
    public void Cancel_RestoresRegionBeforeMove()
    {
        RegionEditor editor = Configured();
        NormalizedRegion original = editor.GetRegion(RegionType.Clock)!;
        editor.BeginMove(RegionType.Clock, new NormalizedPoint(0.2, 0.2));
        editor.Update(new NormalizedPoint(0.7, 0.7));

        editor.Cancel();

        Assert.Equal(original, editor.GetRegion(RegionType.Clock));
        Assert.Equal(RegionEditOperation.None, editor.Operation);
    }

    [Fact]
    public void LoadAndMarkSaved_ControlUnsavedState()
    {
        RegionEditor editor = Configured();
        editor.MarkSaved();
        Assert.False(editor.HasUnsavedChanges);

        editor.SetRegion(RegionType.Clock, new NormalizedRegion(0.1, 0.1, 0.2, 0.2));
        Assert.True(editor.HasUnsavedChanges);

        editor.MarkSaved();
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void Clear_RemovesOnlyRequestedRegion()
    {
        RegionEditor editor = Configured();

        NormalizedRegion? removed = editor.Clear(RegionType.Clock);

        Assert.NotNull(removed);
        Assert.Null(editor.GetRegion(RegionType.Clock));
        Assert.NotNull(editor.GetRegion(RegionType.Minimap));
        Assert.True(editor.HasUnsavedChanges);
    }

    private static RegionEditor Configured()
    {
        RegionEditor editor = new();
        editor.SetRegion(RegionType.Clock, new NormalizedRegion(0.2, 0.2, 0.2, 0.2));
        editor.SetRegion(RegionType.Minimap, new NormalizedRegion(0.7, 0.7, 0.2, 0.2));
        return editor;
    }
}
