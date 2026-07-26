using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Core.Regions;

public enum RegionEditOperation
{
    None,
    Create,
    Move,
    Resize
}

public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left
}

public sealed record RegionEditResult(
    RegionType RegionType,
    RegionEditOperation Operation,
    NormalizedRegion? Before,
    NormalizedRegion? After);

public sealed class RegionEditor
{
    private readonly double _minimumWidth;
    private readonly double _minimumHeight;
    private NormalizedRegion? _clock;
    private NormalizedRegion? _minimap;
    private NormalizedRegion? _savedClock;
    private NormalizedRegion? _savedMinimap;
    private NormalizedRegion? _beforeEdit;
    private NormalizedPoint _dragStart;
    private RegionType? _editingType;
    private ResizeHandle? _resizeHandle;

    public RegionEditor(double minimumWidth = 0.01, double minimumHeight = 0.01)
    {
        if (!double.IsFinite(minimumWidth) || minimumWidth <= 0 || minimumWidth > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        }

        if (!double.IsFinite(minimumHeight) || minimumHeight <= 0 || minimumHeight > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumHeight));
        }

        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
    }

    public RegionEditOperation Operation { get; private set; }

    public RegionType? SelectedRegionType { get; private set; }

    public bool HasUnsavedChanges => _clock != _savedClock || _minimap != _savedMinimap;

    public NormalizedRegion? GetRegion(RegionType type) =>
        type == RegionType.Clock ? _clock : _minimap;

    public void Select(RegionType? type)
    {
        if (Operation != RegionEditOperation.None)
        {
            throw new InvalidOperationException("Cannot change selection during an edit.");
        }

        SelectedRegionType = type;
    }

    public void SetRegion(RegionType type, NormalizedRegion? region)
    {
        EnsureNotEditing();
        Set(type, region);
        SelectedRegionType = region is null && SelectedRegionType == type ? null : type;
    }

    public void Load(NormalizedRegion clock, NormalizedRegion minimap)
    {
        EnsureNotEditing();
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _minimap = minimap ?? throw new ArgumentNullException(nameof(minimap));
        _savedClock = clock;
        _savedMinimap = minimap;
        SelectedRegionType = null;
    }

    public void MarkSaved()
    {
        EnsureNotEditing();
        _savedClock = _clock;
        _savedMinimap = _minimap;
    }

    public void BeginCreate(RegionType type, NormalizedPoint start)
    {
        ValidatePoint(start);
        Begin(type, RegionEditOperation.Create, start, null);
        Set(type, CreateRegion(start, start));
    }

    public void BeginMove(RegionType type, NormalizedPoint start)
    {
        ValidatePoint(start);
        RequireRegion(type);
        Begin(type, RegionEditOperation.Move, start, null);
    }

    public void BeginResize(RegionType type, ResizeHandle handle, NormalizedPoint start)
    {
        ValidatePoint(start);
        RequireRegion(type);
        Begin(type, RegionEditOperation.Resize, start, handle);
    }

    public void Update(NormalizedPoint point)
    {
        ValidatePoint(point);
        if (_editingType is not RegionType type || Operation == RegionEditOperation.None)
        {
            throw new InvalidOperationException("No edit is in progress.");
        }

        NormalizedRegion updated = Operation switch
        {
            RegionEditOperation.Create => CreateRegion(_dragStart, point),
            RegionEditOperation.Move => MoveRegion(_beforeEdit!, point.X - _dragStart.X, point.Y - _dragStart.Y),
            RegionEditOperation.Resize => ResizeRegion(_beforeEdit!, _resizeHandle!.Value, point),
            _ => throw new InvalidOperationException()
        };
        Set(type, updated);
    }

    public RegionEditResult Commit()
    {
        if (_editingType is not RegionType type || Operation == RegionEditOperation.None)
        {
            throw new InvalidOperationException("No edit is in progress.");
        }

        RegionEditResult result = new(type, Operation, _beforeEdit, GetRegion(type));
        EndEdit();
        return result;
    }

    public void Cancel()
    {
        if (_editingType is not RegionType type || Operation == RegionEditOperation.None)
        {
            return;
        }

        Set(type, _beforeEdit);
        EndEdit();
    }

    public NormalizedRegion? Clear(RegionType type)
    {
        EnsureNotEditing();
        NormalizedRegion? previous = GetRegion(type);
        Set(type, null);
        if (SelectedRegionType == type)
        {
            SelectedRegionType = null;
        }

        return previous;
    }

    private void Begin(
        RegionType type,
        RegionEditOperation operation,
        NormalizedPoint start,
        ResizeHandle? resizeHandle)
    {
        EnsureNotEditing();
        SelectedRegionType = type;
        _editingType = type;
        Operation = operation;
        _dragStart = start;
        _beforeEdit = GetRegion(type);
        _resizeHandle = resizeHandle;
    }

    private NormalizedRegion CreateRegion(NormalizedPoint start, NormalizedPoint current)
    {
        double left = Math.Min(start.X, current.X);
        double top = Math.Min(start.Y, current.Y);
        double right = Math.Max(start.X, current.X);
        double bottom = Math.Max(start.Y, current.Y);
        if (right - left < _minimumWidth)
        {
            right = Math.Min(1, left + _minimumWidth);
            left = Math.Max(0, right - _minimumWidth);
        }

        if (bottom - top < _minimumHeight)
        {
            bottom = Math.Min(1, top + _minimumHeight);
            top = Math.Max(0, bottom - _minimumHeight);
        }

        return FromEdges(left, top, right, bottom);
    }

    private static NormalizedRegion MoveRegion(NormalizedRegion region, double dx, double dy)
    {
        double x = Math.Clamp(region.X + dx, 0, 1 - region.Width);
        double y = Math.Clamp(region.Y + dy, 0, 1 - region.Height);
        return new NormalizedRegion(x, y, region.Width, region.Height);
    }

    private NormalizedRegion ResizeRegion(
        NormalizedRegion region,
        ResizeHandle handle,
        NormalizedPoint point)
    {
        double left = region.X;
        double top = region.Y;
        double right = region.X + region.Width;
        double bottom = region.Y + region.Height;

        if (handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft)
        {
            left = Math.Clamp(point.X, 0, right - _minimumWidth);
        }

        if (handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight)
        {
            right = Math.Clamp(point.X, left + _minimumWidth, 1);
        }

        if (handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight)
        {
            top = Math.Clamp(point.Y, 0, bottom - _minimumHeight);
        }

        if (handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight)
        {
            bottom = Math.Clamp(point.Y, top + _minimumHeight, 1);
        }

        return FromEdges(left, top, right, bottom);
    }

    private static NormalizedRegion FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    private void RequireRegion(RegionType type)
    {
        if (GetRegion(type) is null)
        {
            throw new InvalidOperationException($"{type} is not configured.");
        }
    }

    private static void ValidatePoint(NormalizedPoint point)
    {
        if (!point.IsInBounds)
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }
    }

    private void Set(RegionType type, NormalizedRegion? region)
    {
        if (type == RegionType.Clock)
        {
            _clock = region;
        }
        else
        {
            _minimap = region;
        }
    }

    private void EndEdit()
    {
        Operation = RegionEditOperation.None;
        _editingType = null;
        _resizeHandle = null;
        _beforeEdit = null;
    }

    private void EnsureNotEditing()
    {
        if (Operation != RegionEditOperation.None)
        {
            throw new InvalidOperationException("An edit is already in progress.");
        }
    }
}
