using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Core.Regions;

public readonly record struct CoordinateSize(double Width, double Height)
{
    public bool IsValid =>
        double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}

public readonly record struct CoordinatePoint(double X, double Y);

public readonly record struct CoordinateRect(double X, double Y, double Width, double Height);

public readonly record struct NormalizedPoint(double X, double Y)
{
    public bool IsInBounds =>
        double.IsFinite(X) && double.IsFinite(Y) && X >= 0 && X <= 1 && Y >= 0 && Y <= 1;
}

public readonly record struct PreviewViewport(double X, double Y, double Width, double Height)
{
    public bool Contains(CoordinatePoint point) =>
        point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
}

public interface IPreviewCoordinateMapper
{
    PreviewViewport CalculateViewport(CoordinateSize sourceSize, CoordinateSize previewSize);

    CoordinatePoint NormalizedToPreview(
        NormalizedPoint point,
        CoordinateSize sourceSize,
        CoordinateSize previewSize);

    NormalizedPoint? PreviewToNormalized(
        CoordinatePoint point,
        CoordinateSize sourceSize,
        CoordinateSize previewSize);

    CoordinateRect NormalizedRegionToPreview(
        NormalizedRegion region,
        CoordinateSize sourceSize,
        CoordinateSize previewSize);
}

public sealed class PreviewCoordinateMapper : IPreviewCoordinateMapper
{
    public PreviewViewport CalculateViewport(CoordinateSize sourceSize, CoordinateSize previewSize)
    {
        Validate(sourceSize, nameof(sourceSize));
        Validate(previewSize, nameof(previewSize));

        double scale = Math.Min(
            previewSize.Width / sourceSize.Width,
            previewSize.Height / sourceSize.Height);
        double width = sourceSize.Width * scale;
        double height = sourceSize.Height * scale;
        return new PreviewViewport(
            (previewSize.Width - width) / 2,
            (previewSize.Height - height) / 2,
            width,
            height);
    }

    public CoordinatePoint NormalizedToPreview(
        NormalizedPoint point,
        CoordinateSize sourceSize,
        CoordinateSize previewSize)
    {
        if (!point.IsInBounds)
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }

        PreviewViewport viewport = CalculateViewport(sourceSize, previewSize);
        return new CoordinatePoint(
            viewport.X + (point.X * viewport.Width),
            viewport.Y + (point.Y * viewport.Height));
    }

    public NormalizedPoint? PreviewToNormalized(
        CoordinatePoint point,
        CoordinateSize sourceSize,
        CoordinateSize previewSize)
    {
        PreviewViewport viewport = CalculateViewport(sourceSize, previewSize);
        if (!viewport.Contains(point))
        {
            return null;
        }

        return new NormalizedPoint(
            Math.Clamp((point.X - viewport.X) / viewport.Width, 0, 1),
            Math.Clamp((point.Y - viewport.Y) / viewport.Height, 0, 1));
    }

    public CoordinateRect NormalizedRegionToPreview(
        NormalizedRegion region,
        CoordinateSize sourceSize,
        CoordinateSize previewSize)
    {
        ArgumentNullException.ThrowIfNull(region);
        PreviewViewport viewport = CalculateViewport(sourceSize, previewSize);
        return new CoordinateRect(
            viewport.X + (region.X * viewport.Width),
            viewport.Y + (region.Y * viewport.Height),
            region.Width * viewport.Width,
            region.Height * viewport.Height);
    }

    private static void Validate(CoordinateSize size, string parameterName)
    {
        if (!size.IsValid)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Dimensions must be finite and positive.");
        }
    }
}
