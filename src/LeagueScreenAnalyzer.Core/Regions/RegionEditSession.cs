using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Core.Regions;

public sealed record PixelGeometryUpdate(bool IsValid, string? Error, PixelRegion Geometry);

/// <summary>
/// Transactional, source-pixel precision editing. The top-left corner is the
/// stable anchor for keyboard and numeric resizing.
/// </summary>
public sealed class RegionEditSession
{
    private readonly IRegionShapePolicy _shapePolicy;
    private readonly RegionSourceSize _sourceSize;

    public RegionEditSession(
        RegionType selectedRegionType,
        NormalizedRegion originalRegion,
        RegionSourceSize sourceSize,
        IRegionShapePolicy? shapePolicy = null,
        double? zoom = null)
    {
        SelectedRegionType = selectedRegionType;
        OriginalRegion = originalRegion ?? throw new ArgumentNullException(nameof(originalRegion));
        WorkingRegion = originalRegion;
        _sourceSize = sourceSize.Validate();
        _shapePolicy = shapePolicy ?? new SemanticRegionShapePolicy();
        Zoom = zoom ?? (selectedRegionType == RegionType.Clock ? 8 : 4);
    }

    public NormalizedRegion OriginalRegion { get; }
    public NormalizedRegion WorkingRegion { get; private set; }
    public RegionType SelectedRegionType { get; }
    public bool IsDirty => WorkingRegion != OriginalRegion;
    public double Zoom { get; }
    public PixelRegion PixelGeometry => PixelRegionGeometry.ToPixels(WorkingRegion, _sourceSize);
    public RegionGeometryValidation Validation =>
        _shapePolicy.Validate(SelectedRegionType, WorkingRegion, _sourceSize);

    public PixelGeometryUpdate SetPixelGeometry(int x, int y, int width, int height)
    {
        int sourceWidth = (int)Math.Round(_sourceSize.Width);
        int sourceHeight = (int)Math.Round(_sourceSize.Height);
        if (width < 1 || height < 1)
        {
            return Invalid("Width, height, and size must be at least 1 source pixel.");
        }

        if (SelectedRegionType == RegionType.Minimap && width != height)
        {
            return Invalid("MINIMAP Size must produce equal source-pixel width and height.");
        }

        if (x < 0 || y < 0 || x + width > sourceWidth || y + height > sourceHeight)
        {
            return Invalid(
                $"Region must remain inside the {sourceWidth} × {sourceHeight} source.");
        }

        PixelRegion proposedPixels = new(x, y, width, height);
        NormalizedRegion proposed = PixelRegionGeometry.FromPixels(proposedPixels, _sourceSize);
        RegionGeometryValidation validation =
            _shapePolicy.ValidateStrict(SelectedRegionType, proposed, _sourceSize);
        if (!validation.IsValid)
        {
            return new PixelGeometryUpdate(false, validation.Error, PixelGeometry);
        }

        WorkingRegion = proposed;
        return new PixelGeometryUpdate(true, null, PixelGeometry);
    }

    public PixelGeometryUpdate SetPixelGeometry(int x, int y, int size) =>
        SetPixelGeometry(x, y, size, size);

    public PixelGeometryUpdate MoveByPixels(int deltaX, int deltaY)
    {
        PixelRegion current = PixelGeometry;
        int sourceWidth = (int)Math.Round(_sourceSize.Width);
        int sourceHeight = (int)Math.Round(_sourceSize.Height);
        int x = Math.Clamp(current.X + deltaX, 0, sourceWidth - current.Width);
        int y = Math.Clamp(current.Y + deltaY, 0, sourceHeight - current.Height);
        return SetPixelGeometry(x, y, current.Width, current.Height);
    }

    public PixelGeometryUpdate ResizeByPixels(int horizontalDelta, int verticalDelta)
    {
        PixelRegion current = PixelGeometry;
        if (SelectedRegionType == RegionType.Minimap)
        {
            int delta = horizontalDelta != 0 ? horizontalDelta : verticalDelta;
            int maximum = Math.Min(
                (int)Math.Round(_sourceSize.Width) - current.X,
                (int)Math.Round(_sourceSize.Height) - current.Y);
            int size = Math.Clamp(current.Width + delta, 1, maximum);
            return SetPixelGeometry(current.X, current.Y, size, size);
        }

        int width = Math.Clamp(
            current.Width + horizontalDelta,
            1,
            (int)Math.Round(_sourceSize.Width) - current.X);
        int height = Math.Clamp(
            current.Height + verticalDelta,
            1,
            (int)Math.Round(_sourceSize.Height) - current.Y);

        if (horizontalDelta == 0 && width < Math.Ceiling(height * 2d))
        {
            height = Math.Max(1, width / 2);
        }
        else if (verticalDelta == 0 && width < Math.Ceiling(height * 2d))
        {
            width = Math.Min(
                (int)Math.Ceiling(height * 2d),
                (int)Math.Round(_sourceSize.Width) - current.X);
            if (width < Math.Ceiling(height * 2d))
            {
                height = width / 2;
            }
        }

        return SetPixelGeometry(current.X, current.Y, width, height);
    }

    public NormalizedRegion Apply()
    {
        RegionGeometryValidation validation =
            _shapePolicy.ValidateStrict(SelectedRegionType, WorkingRegion, _sourceSize);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Error);
        }

        return WorkingRegion;
    }

    public NormalizedRegion Cancel()
    {
        WorkingRegion = OriginalRegion;
        return OriginalRegion;
    }

    private PixelGeometryUpdate Invalid(string error) =>
        new(false, error, PixelGeometry);
}
