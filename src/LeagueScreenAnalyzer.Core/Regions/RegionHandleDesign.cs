using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Core.Regions;

public static class RegionHandleDesign
{
    public const double VisibleSize = 6;
    public const double HitTargetSize = 18;

    private static readonly ResizeHandle[] ClockHandles =
    [
        ResizeHandle.TopLeft,
        ResizeHandle.Top,
        ResizeHandle.TopRight,
        ResizeHandle.Right,
        ResizeHandle.BottomRight,
        ResizeHandle.Bottom,
        ResizeHandle.BottomLeft,
        ResizeHandle.Left
    ];

    private static readonly ResizeHandle[] MinimapHandles =
    [
        ResizeHandle.TopLeft,
        ResizeHandle.TopRight,
        ResizeHandle.BottomRight,
        ResizeHandle.BottomLeft
    ];

    public static IReadOnlyList<ResizeHandle> For(RegionType type) =>
        type == RegionType.Clock ? ClockHandles : MinimapHandles;
}
