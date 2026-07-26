using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Core.Regions;

public readonly record struct RegionSourceSize(double Width, double Height)
{
    public static RegionSourceSize Unit { get; } = new(1, 1);

    public RegionSourceSize Validate()
    {
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RegionSourceSize));
        }

        return this;
    }
}

public sealed record RegionGeometryValidation(
    bool IsValid,
    string? Error,
    double PixelAspectRatio);

public sealed record LegacyRegionNormalization(
    NormalizedRegion Region,
    bool WasNormalized,
    string? Warning);

public interface IRegionShapePolicy
{
    NormalizedRegion ConstrainCreate(
        RegionType regionType,
        NormalizedRegion proposed,
        RegionSourceSize sourceSize);

    NormalizedRegion ConstrainResize(
        RegionType regionType,
        NormalizedRegion original,
        NormalizedRegion proposed,
        ResizeHandle handle,
        RegionSourceSize sourceSize);

    RegionGeometryValidation Validate(
        RegionType regionType,
        NormalizedRegion region,
        RegionSourceSize sourceSize);

    LegacyRegionNormalization NormalizeLegacy(
        RegionType regionType,
        NormalizedRegion region,
        RegionSourceSize sourceSize);
}

public sealed class SemanticRegionShapePolicy(
    double minimumClockWidthToHeightRatio = 2.0,
    double minimapRoundingTolerance = 0.025,
    double minimapValidationTolerance = 0.01) : IRegionShapePolicy
{
    public double MinimumClockWidthToHeightRatio { get; } =
        ValidatePositive(minimumClockWidthToHeightRatio, nameof(minimumClockWidthToHeightRatio));

    public double MinimapRoundingTolerance { get; } =
        ValidateNonNegative(minimapRoundingTolerance, nameof(minimapRoundingTolerance));

    public double MinimapValidationTolerance { get; } =
        ValidateNonNegative(minimapValidationTolerance, nameof(minimapValidationTolerance));

    public NormalizedRegion ConstrainCreate(
        RegionType regionType,
        NormalizedRegion proposed,
        RegionSourceSize sourceSize) =>
        regionType == RegionType.Minimap
            ? SquareFromTopLeft(proposed, sourceSize.Validate())
            : ConstrainClock(proposed, sourceSize.Validate(), ResizeHandle.BottomRight);

    public NormalizedRegion ConstrainResize(
        RegionType regionType,
        NormalizedRegion original,
        NormalizedRegion proposed,
        ResizeHandle handle,
        RegionSourceSize sourceSize) =>
        regionType == RegionType.Minimap
            ? ConstrainSquareResize(original, proposed, handle, sourceSize.Validate())
            : ConstrainClock(proposed, sourceSize.Validate(), handle);

    public RegionGeometryValidation Validate(
        RegionType regionType,
        NormalizedRegion region,
        RegionSourceSize sourceSize)
    {
        RegionSourceSize source = sourceSize.Validate();
        double ratio = PixelAspectRatio(region, source);
        if (regionType == RegionType.Minimap)
        {
            double deviation = Math.Abs(ratio - 1);
            return deviation <= MinimapValidationTolerance
                ? new RegionGeometryValidation(true, null, ratio)
                : new RegionGeometryValidation(
                    false,
                    $"MINIMAP must be square in source pixels (current ratio {ratio:0.###}:1).",
                    ratio);
        }

        return ratio >= MinimumClockWidthToHeightRatio
            ? new RegionGeometryValidation(true, null, ratio)
            : new RegionGeometryValidation(
                false,
                $"CLOCK must be a wide horizontal region with width-to-height ratio of at least {MinimumClockWidthToHeightRatio:0.##}:1 (current ratio {ratio:0.###}:1).",
                ratio);
    }

    public LegacyRegionNormalization NormalizeLegacy(
        RegionType regionType,
        NormalizedRegion region,
        RegionSourceSize sourceSize)
    {
        if (regionType != RegionType.Minimap)
        {
            RegionGeometryValidation validation = Validate(regionType, region, sourceSize);
            return new LegacyRegionNormalization(region, false, validation.Error);
        }

        RegionSourceSize source = sourceSize.Validate();
        double ratio = PixelAspectRatio(region, source);
        double deviation = Math.Abs(ratio - 1);
        if (deviation <= MinimapValidationTolerance)
        {
            return new LegacyRegionNormalization(region, false, null);
        }

        if (deviation > MinimapRoundingTolerance)
        {
            return new LegacyRegionNormalization(
                region,
                false,
                $"Loaded MINIMAP is materially non-square ({ratio:0.###}:1); it was retained for manual correction.");
        }

        double sidePixels = ((region.Width * source.Width) + (region.Height * source.Height)) / 2;
        NormalizedRegion normalized = SquareCentered(
            region.X + (region.Width / 2),
            region.Y + (region.Height / 2),
            sidePixels,
            source);
        return new LegacyRegionNormalization(
            normalized,
            true,
            $"Loaded MINIMAP rounding deviation ({ratio:0.###}:1) was normalized to a source-pixel square.");
    }

    public static double PixelAspectRatio(NormalizedRegion region, RegionSourceSize sourceSize) =>
        (region.Width * sourceSize.Width) / (region.Height * sourceSize.Height);

    private NormalizedRegion ConstrainSquareResize(
        NormalizedRegion original,
        NormalizedRegion proposed,
        ResizeHandle handle,
        RegionSourceSize source)
    {
        double widthPixels = proposed.Width * source.Width;
        double heightPixels = proposed.Height * source.Height;
        double sidePixels = handle is ResizeHandle.Left or ResizeHandle.Right
            ? widthPixels
            : handle is ResizeHandle.Top or ResizeHandle.Bottom
                ? heightPixels
                : Math.Max(widthPixels, heightPixels);

        double oppositeX = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft =>
                original.X + original.Width,
            ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight => original.X,
            _ => original.X + (original.Width / 2)
        };
        double oppositeY = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight =>
                original.Y + original.Height,
            ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight => original.Y,
            _ => original.Y + (original.Height / 2)
        };

        double width = Math.Min(sidePixels / source.Width, 1);
        double height = Math.Min(sidePixels / source.Height, 1);
        double limitingScale = Math.Min(1, Math.Min(1 / width, 1 / height));
        width *= limitingScale;
        height *= limitingScale;

        double x = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft => oppositeX - width,
            ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight => oppositeX,
            _ => oppositeX - (width / 2)
        };
        double y = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight => oppositeY - height,
            ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight => oppositeY,
            _ => oppositeY - (height / 2)
        };

        x = Math.Clamp(x, 0, 1 - width);
        y = Math.Clamp(y, 0, 1 - height);
        return new NormalizedRegion(x, y, width, height);
    }

    private NormalizedRegion SquareFromTopLeft(
        NormalizedRegion proposed,
        RegionSourceSize source)
    {
        double sidePixels = Math.Max(
            proposed.Width * source.Width,
            proposed.Height * source.Height);
        sidePixels = Math.Min(sidePixels, Math.Min(source.Width, source.Height));
        double width = sidePixels / source.Width;
        double height = sidePixels / source.Height;
        return new NormalizedRegion(
            Math.Clamp(proposed.X, 0, 1 - width),
            Math.Clamp(proposed.Y, 0, 1 - height),
            width,
            height);
    }

    private NormalizedRegion ConstrainClock(
        NormalizedRegion proposed,
        RegionSourceSize source,
        ResizeHandle handle)
    {
        double ratio = PixelAspectRatio(proposed, source);
        if (ratio >= MinimumClockWidthToHeightRatio)
        {
            return proposed;
        }

        double requiredWidth = proposed.Height * source.Height *
            MinimumClockWidthToHeightRatio / source.Width;
        if (requiredWidth <= 1)
        {
            double x = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft
                ? proposed.X + proposed.Width - requiredWidth
                : proposed.X;
            x = Math.Clamp(x, 0, 1 - requiredWidth);
            return new NormalizedRegion(x, proposed.Y, requiredWidth, proposed.Height);
        }

        double allowedHeight = source.Width /
            (MinimumClockWidthToHeightRatio * source.Height);
        double y = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight
            ? proposed.Y + proposed.Height - allowedHeight
            : proposed.Y;
        return new NormalizedRegion(0, Math.Clamp(y, 0, 1 - allowedHeight), 1, allowedHeight);
    }

    private static NormalizedRegion SquareCentered(
        double centerX,
        double centerY,
        double sidePixels,
        RegionSourceSize source)
    {
        sidePixels = Math.Min(sidePixels, Math.Min(source.Width, source.Height));
        double width = sidePixels / source.Width;
        double height = sidePixels / source.Height;
        return new NormalizedRegion(
            Math.Clamp(centerX - (width / 2), 0, 1 - width),
            Math.Clamp(centerY - (height / 2), 0, 1 - height),
            width,
            height);
    }

    private static double ValidatePositive(double value, string name) =>
        double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(name);

    private static double ValidateNonNegative(double value, string name) =>
        double.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(name);
}
