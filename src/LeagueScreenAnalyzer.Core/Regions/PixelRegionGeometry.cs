using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Core.Regions;

public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Defines the single normalized-coordinate to source-pixel conversion used by
/// editing, crop extraction, and semantic validation.
/// </summary>
public static class PixelRegionGeometry
{
    private const double EndpointEpsilon = 1e-9;

    public static PixelRegion ToPixels(
        NormalizedRegion region,
        RegionSourceSize sourceSize)
    {
        RegionSourceSize source = sourceSize.Validate();
        int sourceWidth = checked((int)Math.Round(source.Width));
        int sourceHeight = checked((int)Math.Round(source.Height));
        int left = Math.Clamp(
            (int)Math.Floor((region.X * sourceWidth) + EndpointEpsilon),
            0,
            sourceWidth - 1);
        int top = Math.Clamp(
            (int)Math.Floor((region.Y * sourceHeight) + EndpointEpsilon),
            0,
            sourceHeight - 1);
        int right = Math.Clamp(
            (int)Math.Ceiling(((region.X + region.Width) * sourceWidth) - EndpointEpsilon),
            left + 1,
            sourceWidth);
        int bottom = Math.Clamp(
            (int)Math.Ceiling(((region.Y + region.Height) * sourceHeight) - EndpointEpsilon),
            top + 1,
            sourceHeight);
        return new PixelRegion(left, top, right - left, bottom - top);
    }

    public static NormalizedRegion FromPixels(
        PixelRegion region,
        RegionSourceSize sourceSize)
    {
        RegionSourceSize source = sourceSize.Validate();
        int sourceWidth = checked((int)Math.Round(source.Width));
        int sourceHeight = checked((int)Math.Round(source.Height));
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            region.Right > sourceWidth || region.Bottom > sourceHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                "Pixel region must have positive dimensions and remain inside the source.");
        }

        return new NormalizedRegion(
            (double)region.X / sourceWidth,
            (double)region.Y / sourceHeight,
            (double)region.Width / sourceWidth,
            (double)region.Height / sourceHeight);
    }
}
