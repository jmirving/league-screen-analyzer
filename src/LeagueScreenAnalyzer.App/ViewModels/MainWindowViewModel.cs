using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LeagueScreenAnalyzer.App.Services;
using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;
using LeagueScreenAnalyzer.Storage;
using Microsoft.Extensions.Logging;

namespace LeagueScreenAnalyzer.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    public const double MaterialAspectRatioThreshold = 0.02;
    private readonly CaptureController _captureController;
    private readonly IWindowHandleProvider _windowHandleProvider;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IPreviewCoordinateMapper _coordinateMapper;
    private readonly RegionEditor _regionEditor;
    private readonly ICaptureLayoutStore _layoutStore;
    private readonly ISourceAspectRatioCompatibility _aspectRatioCompatibility;
    private CaptureState _captureState;
    private WriteableBitmap? _previewImage;
    private readonly CropBitmapCache _clockCropCache = new();
    private readonly CropBitmapCache _minimapCropCache = new();
    private string? _diagnosticMessage;
    private string? _compatibilityWarning;
    private string? _layoutValidationMessage;
    private string _layoutName = string.Empty;
    private string? _selectedSavedLayout;
    private bool _overwriteLayout;
    private RegionType? _activeEditType;
    private double _previewWidth;
    private double _previewHeight;
    private double? _layoutAspectRatio;
    private bool _disposed;

    public MainWindowViewModel(
        CaptureController captureController,
        IWindowHandleProvider windowHandleProvider,
        Dispatcher dispatcher,
        ILogger<MainWindowViewModel> logger,
        ICaptureLayoutStore? layoutStore = null,
        IPreviewCoordinateMapper? coordinateMapper = null,
        RegionEditor? regionEditor = null,
        ISourceAspectRatioCompatibility? aspectRatioCompatibility = null)
    {
        _captureController = captureController ?? throw new ArgumentNullException(nameof(captureController));
        _windowHandleProvider = windowHandleProvider ?? throw new ArgumentNullException(nameof(windowHandleProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _layoutStore = layoutStore ?? new JsonCaptureLayoutStore();
        _coordinateMapper = coordinateMapper ?? new PreviewCoordinateMapper();
        _regionEditor = regionEditor ?? new RegionEditor();
        _aspectRatioCompatibility = aspectRatioCompatibility
            ?? new SourceAspectRatioCompatibility(MaterialAspectRatioThreshold);
        _captureState = captureController.State;

        ClockOverlay = new RegionOverlayViewModel(RegionType.Clock);
        MinimapOverlay = new RegionOverlayViewModel(RegionType.Minimap);
        ClockCrop = new CropPreviewViewModel("CLOCK");
        MinimapCrop = new CropPreviewViewModel("MINIMAP");
        SelectWindowCommand = new AsyncRelayCommand(
            SelectWindowAsync, () => _captureState.CanSelect, SetCommandError);
        StopCaptureCommand = new AsyncRelayCommand(
            () => _captureController.StopAsync(), () => _captureState.IsCapturing, SetCommandError);
        SaveDiagnosticFrameCommand = new RelayCommand(
            SaveDiagnosticBundle, () => _previewImage is not null);
        EditClockCommand = new RelayCommand(
            () => ActivateEditor(RegionType.Clock), () => CanEditRegions);
        EditMinimapCommand = new RelayCommand(
            () => ActivateEditor(RegionType.Minimap), () => CanEditRegions);
        ClearClockCommand = new RelayCommand(
            () => ClearRegion(RegionType.Clock), () => CanEditRegions && ClockRegion is not null);
        ClearMinimapCommand = new RelayCommand(
            () => ClearRegion(RegionType.Minimap), () => CanEditRegions && MinimapRegion is not null);
        SaveLayoutCommand = new AsyncRelayCommand(
            SaveLayoutAsync, () => BothRegionsValid && !string.IsNullOrWhiteSpace(LayoutName), SetCommandError);
        LoadLayoutCommand = new AsyncRelayCommand(
            LoadLayoutAsync, () => !string.IsNullOrWhiteSpace(SelectedSavedLayout), SetCommandError);
        DeleteLayoutCommand = new AsyncRelayCommand(
            DeleteLayoutAsync, () => !string.IsNullOrWhiteSpace(SelectedSavedLayout), SetCommandError);

        _captureController.StateChanged += OnCaptureStateChanged;
        _captureController.FrameArrived += OnFrameArrived;
        _ = RefreshLayoutsAsync();
    }

    public string Title => "League Screen Analyzer";
    public string MilestoneDescription => "Normalized Clock and Minimap capture regions";
    public string CaptureStatus => _captureState.Status.ToString();
    public string SelectedSourceName => _captureState.SourceName ?? "No window selected";
    public string FrameDimensions => _captureState.Width > 0 && _captureState.Height > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{_captureState.Width} × {_captureState.Height}")
        : "—";
    public string LatestFrame => _captureState.LatestSequence is long sequence
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"Sequence {sequence} · {_captureState.LatestTimestamp:hh\\:mm\\:ss\\.fff}")
        : "—";
    public string? ErrorMessage => _captureState.ErrorMessage;
    public string? DiagnosticMessage => _diagnosticMessage;
    public string? CompatibilityWarning => _compatibilityWarning;
    public string? LayoutValidationMessage => _layoutValidationMessage;
    public ImageSource? PreviewImage => _previewImage;
    public bool CanEditRegions => _captureState.IsCapturing && _captureState.Width > 0;
    public bool BothRegionsValid => ClockRegion is not null && MinimapRegion is not null;
    public string RegionValidity => BothRegionsValid
        ? "Both required regions are valid."
        : "Configure both CLOCK and MINIMAP before saving.";
    public bool HasUnsavedChanges => _regionEditor.HasUnsavedChanges;
    public string UnsavedStatus => HasUnsavedChanges ? "Unsaved changes" : "Saved";
    public NormalizedRegion? ClockRegion => _regionEditor.GetRegion(RegionType.Clock);
    public NormalizedRegion? MinimapRegion => _regionEditor.GetRegion(RegionType.Minimap);
    public string ClockCoordinates => FormatRegion(ClockRegion);
    public string MinimapCoordinates => FormatRegion(MinimapRegion);
    public RegionType? SelectedRegionType => _regionEditor.SelectedRegionType;
    public string EditMode => _activeEditType is RegionType type
        ? $"Editing {type.ToString().ToUpperInvariant()}"
        : "Editing off";

    public string LayoutName
    {
        get => _layoutName;
        set
        {
            if (Set(ref _layoutName, value))
            {
                SaveLayoutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedSavedLayout
    {
        get => _selectedSavedLayout;
        set
        {
            if (Set(ref _selectedSavedLayout, value))
            {
                LoadLayoutCommand.RaiseCanExecuteChanged();
                DeleteLayoutCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool OverwriteLayout { get => _overwriteLayout; set => Set(ref _overwriteLayout, value); }
    public ObservableCollection<string> AvailableSavedLayouts { get; } = [];
    public RegionOverlayViewModel ClockOverlay { get; }
    public RegionOverlayViewModel MinimapOverlay { get; }
    public CropPreviewViewModel ClockCrop { get; }
    public CropPreviewViewModel MinimapCrop { get; }
    public AsyncRelayCommand SelectWindowCommand { get; }
    public AsyncRelayCommand StopCaptureCommand { get; }
    public RelayCommand SaveDiagnosticFrameCommand { get; }
    public RelayCommand EditClockCommand { get; }
    public RelayCommand EditMinimapCommand { get; }
    public RelayCommand ClearClockCommand { get; }
    public RelayCommand ClearMinimapCommand { get; }
    public AsyncRelayCommand SaveLayoutCommand { get; }
    public AsyncRelayCommand LoadLayoutCommand { get; }
    public AsyncRelayCommand DeleteLayoutCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetPreviewSize(double width, double height)
    {
        _previewWidth = width;
        _previewHeight = height;
        UpdateOverlayGeometry();
    }

    public bool PointerDown(double x, double y)
    {
        if (!CanEditRegions || !TryMap(x, y, out NormalizedPoint point))
        {
            if (_regionEditor.Operation == RegionEditOperation.None)
            {
                _regionEditor.Select(null);
                RaiseRegionState();
            }

            return false;
        }

        RegionType? hitType = HitRegion(x, y);
        if (hitType is RegionType hit)
        {
            _activeEditType = hit;
            _regionEditor.Select(hit);
            ResizeHandle? handle = HitHandle(hit, x, y);
            if (handle is ResizeHandle resizeHandle)
            {
                _regionEditor.BeginResize(hit, resizeHandle, point);
            }
            else
            {
                _regionEditor.BeginMove(hit, point);
            }

            RaiseRegionState();
            return true;
        }

        if (_activeEditType is RegionType active && _regionEditor.GetRegion(active) is null)
        {
            _regionEditor.BeginCreate(active, point);
            RaiseRegionState();
            return true;
        }

        _regionEditor.Select(null);
        RaiseRegionState();
        return false;
    }

    public void PointerMove(double x, double y)
    {
        if (_regionEditor.Operation == RegionEditOperation.None
            || !TryMapClamped(x, y, out NormalizedPoint point))
        {
            return;
        }

        _regionEditor.Update(point);
        RaiseRegionState();
    }

    public void PointerUp()
    {
        if (_regionEditor.Operation == RegionEditOperation.None)
        {
            return;
        }

        RegionEditResult result = _regionEditor.Commit();
        _logger.LogInformation(
            "Region {RegionType} {Operation}: {Region}.",
            result.RegionType,
            result.Operation,
            result.After);
        RaiseRegionState();
    }

    public void CancelEdit()
    {
        _regionEditor.Cancel();
        RaiseRegionState();
    }

    public void DeleteSelectedRegion()
    {
        if (CanEditRegions && SelectedRegionType is RegionType type)
        {
            ClearRegion(type);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _captureController.StateChanged -= OnCaptureStateChanged;
        _captureController.FrameArrived -= OnFrameArrived;
        await _captureController.DisposeAsync();
    }

    private Task SelectWindowAsync()
    {
        _diagnosticMessage = null;
        OnPropertyChanged(nameof(DiagnosticMessage));
        return _captureController.SelectWindowAsync(_windowHandleProvider.GetHandle());
    }

    private void ActivateEditor(RegionType type)
    {
        _layoutAspectRatio ??= CurrentAspectRatio();
        _activeEditType = type;
        _regionEditor.Select(type);
        RaiseRegionState();
    }

    private void ClearRegion(RegionType type)
    {
        _regionEditor.Cancel();
        if (_regionEditor.Clear(type) is not null)
        {
            _logger.LogInformation("Region {RegionType} cleared.", type);
        }

        RaiseRegionState();
    }

    private async Task SaveLayoutAsync()
    {
        if (ClockRegion is not NormalizedRegion clock || MinimapRegion is not NormalizedRegion minimap)
        {
            return;
        }

        double? aspect = CurrentAspectRatio();
        CaptureLayout layout = new(LayoutName.Trim(), clock, minimap, aspect);
        await _layoutStore.SaveAsync(layout, OverwriteLayout);
        _regionEditor.MarkSaved();
        _layoutAspectRatio = aspect;
        SelectedSavedLayout = layout.Name;
        _layoutValidationMessage = $"Saved layout '{layout.Name}'.";
        _logger.LogInformation(
            "Capture layout {LayoutName} saved with overwrite={Overwrite}.",
            layout.Name,
            OverwriteLayout);
        await RefreshLayoutsAsync();
        RaiseRegionState();
        OnPropertyChanged(nameof(LayoutValidationMessage));
    }

    private async Task LoadLayoutAsync()
    {
        string name = SelectedSavedLayout!;
        try
        {
            CaptureLayout layout = await _layoutStore.LoadAsync(name);
            _regionEditor.Cancel();
            _regionEditor.Load(layout.ClockRegion, layout.MinimapRegion);
            _layoutAspectRatio = layout.SourceAspectRatio;
            LayoutName = layout.Name;
            _activeEditType = null;
            _layoutValidationMessage = $"Loaded layout '{layout.Name}'.";
            _logger.LogInformation("Capture layout {LayoutName} loaded.", layout.Name);
            EvaluateCompatibility();
            RaiseRegionState();
        }
        catch (CaptureLayoutException exception)
        {
            _layoutValidationMessage = exception.Message;
            _logger.LogWarning(exception, "Malformed capture layout {LayoutName} rejected.", name);
            OnPropertyChanged(nameof(LayoutValidationMessage));
        }
    }

    private async Task DeleteLayoutAsync()
    {
        string name = SelectedSavedLayout!;
        await _layoutStore.DeleteAsync(name);
        _logger.LogInformation("Capture layout {LayoutName} deleted.", name);
        _layoutValidationMessage = $"Deleted layout '{name}'.";
        SelectedSavedLayout = null;
        await RefreshLayoutsAsync();
        OnPropertyChanged(nameof(LayoutValidationMessage));
    }

    private async Task RefreshLayoutsAsync()
    {
        try
        {
            IReadOnlyList<string> layouts = await _layoutStore.ListAsync();
            RunOnDispatcher(() =>
            {
                AvailableSavedLayouts.Clear();
                foreach (string layout in layouts)
                {
                    AvailableSavedLayouts.Add(layout);
                }
            });
        }
        catch (Exception exception)
        {
            SetCommandError(exception);
        }
    }

    private void OnCaptureStateChanged(object? sender, CaptureStateChangedEventArgs args)
    {
        RunOnDispatcher(() =>
        {
            int oldWidth = _captureState.Width;
            int oldHeight = _captureState.Height;
            _captureState = args.State;
            if ((oldWidth != args.State.Width || oldHeight != args.State.Height)
                && args.State.Width > 0 && args.State.Height > 0)
            {
                EvaluateCompatibility();
                UpdateOverlayGeometry();
            }

            OnPropertyChanged(nameof(CaptureStatus));
            OnPropertyChanged(nameof(SelectedSourceName));
            OnPropertyChanged(nameof(FrameDimensions));
            OnPropertyChanged(nameof(LatestFrame));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(CanEditRegions));
            RaiseCommandStates();
        });
    }

    private void EvaluateCompatibility()
    {
        double? current = CurrentAspectRatio();
        if ((ClockRegion is null && MinimapRegion is null)
            || _layoutAspectRatio is not double expected
            || current is not double actual)
        {
            _compatibilityWarning = null;
        }
        else
        {
            double difference = _aspectRatioCompatibility.CalculateRelativeDifference(expected, actual);
            _compatibilityWarning = _aspectRatioCompatibility.IsMaterialMismatch(expected, actual)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Layout aspect ratio {expected:F4} differs from source {actual:F4} by {difference:P1}. Regions were retained for manual adjustment.")
                : null;
            if (_compatibilityWarning is not null)
            {
                _logger.LogWarning(
                    "Material aspect-ratio mismatch detected. Layout={LayoutAspectRatio}, Source={SourceAspectRatio}, Difference={Difference:P2}.",
                    expected,
                    actual,
                    difference);
            }
        }

        OnPropertyChanged(nameof(CompatibilityWarning));
    }

    private void OnFrameArrived(object? sender, CaptureFrameEventArgs args)
    {
        if (args.Frame.Payload is not Bgra32FramePayload payload)
        {
            return;
        }

        RunOnDispatcher(() => UpdatePreview(args.Frame.Width, args.Frame.Height, payload));
    }

    private unsafe void UpdatePreview(int width, int height, Bgra32FramePayload payload)
    {
        if (_previewImage is null
            || _previewImage.PixelWidth != width
            || _previewImage.PixelHeight != height)
        {
            _previewImage = CreateBitmap(width, height);
            OnPropertyChanged(nameof(PreviewImage));
        }

        Span<byte> pixels = payload.Pixels.Span;
        fixed (byte* pixelPointer = pixels)
        {
            _previewImage.WritePixels(
                new Int32Rect(0, 0, width, height),
                (nint)pixelPointer,
                pixels.Length,
                payload.Stride);
            UpdateCrop(RegionType.Clock, ClockRegion, width, height, payload, pixelPointer);
            UpdateCrop(RegionType.Minimap, MinimapRegion, width, height, payload, pixelPointer);
        }

        SaveDiagnosticFrameCommand.RaiseCanExecuteChanged();
    }

    private unsafe void UpdateCrop(
        RegionType type,
        NormalizedRegion? region,
        int sourceWidth,
        int sourceHeight,
        Bgra32FramePayload payload,
        byte* pixelPointer)
    {
        CropPreviewViewModel preview = type == RegionType.Clock ? ClockCrop : MinimapCrop;
        if (region is null)
        {
            preview.Image = null;
            preview.Dimensions = "Not configured";
            preview.Coordinates = "Not configured";
            return;
        }

        Int32Rect pixelRect = ToPixelRect(region, sourceWidth, sourceHeight);
        CropBitmapCache cache = type == RegionType.Clock ? _clockCropCache : _minimapCropCache;
        WriteableBitmap crop = cache.GetOrCreate(pixelRect.Width, pixelRect.Height);
        preview.Image = crop;

        int offset = (pixelRect.Y * payload.Stride) + (pixelRect.X * 4);
        crop.WritePixels(
            new Int32Rect(0, 0, pixelRect.Width, pixelRect.Height),
            (nint)(pixelPointer + offset),
            payload.Pixels.Length - offset,
            payload.Stride);
        preview.Dimensions = string.Create(
            CultureInfo.InvariantCulture,
            $"{pixelRect.Width} × {pixelRect.Height} px");
        preview.Coordinates = FormatRegion(region);
    }

    private void SaveDiagnosticBundle()
    {
        if (_previewImage is null)
        {
            return;
        }

        try
        {
            string root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts"));
            string directory = Path.Combine(root, $"capture-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss-fff}");
            Directory.CreateDirectory(directory);
            SavePng(_previewImage, Path.Combine(directory, "full-frame.png"));
            SaveAnnotatedFrame(Path.Combine(directory, "annotated-frame.png"));
            if (ClockCrop.Image is BitmapSource clockCropImage)
            {
                SavePng(clockCropImage, Path.Combine(directory, "clock-crop.png"));
            }

            if (MinimapCrop.Image is BitmapSource minimapCropImage)
            {
                SavePng(minimapCropImage, Path.Combine(directory, "minimap-crop.png"));
            }

            SaveDiagnosticLayout(Path.Combine(directory, "active-layout.json"));
            _diagnosticMessage = $"Saved diagnostic bundle: {directory}";
            _logger.LogInformation("Saved diagnostic capture bundle to {Path}.", directory);
        }
        catch (Exception exception)
        {
            _diagnosticMessage = $"Could not save diagnostic bundle: {exception.Message}";
            _logger.LogError(exception, "Failed to save diagnostic capture bundle.");
        }

        OnPropertyChanged(nameof(DiagnosticMessage));
    }

    private void SaveAnnotatedFrame(string path)
    {
        DrawingVisual visual = new();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawImage(_previewImage, new Rect(0, 0, _previewImage!.PixelWidth, _previewImage.PixelHeight));
            DrawRegion(drawing, ClockRegion, "CLOCK", Brushes.Gold);
            DrawRegion(drawing, MinimapRegion, "MINIMAP", Brushes.DeepSkyBlue);
        }

        RenderTargetBitmap rendered = new(
            _previewImage.PixelWidth,
            _previewImage.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        rendered.Render(visual);
        SavePng(rendered, path);
    }

    private void DrawRegion(
        DrawingContext drawing,
        NormalizedRegion? region,
        string label,
        Brush brush)
    {
        if (region is null || _previewImage is null)
        {
            return;
        }

        Rect rect = new(
            region.X * _previewImage.PixelWidth,
            region.Y * _previewImage.PixelHeight,
            region.Width * _previewImage.PixelWidth,
            region.Height * _previewImage.PixelHeight);
        drawing.DrawRectangle(null, new Pen(brush, 3), rect);
        FormattedText text = new(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            18,
            brush,
            1);
        drawing.DrawText(text, new Point(rect.Left + 3, rect.Top + 3));
    }

    private void SaveDiagnosticLayout(string path)
    {
        object document = new
        {
            schemaVersion = 1,
            name = string.IsNullOrWhiteSpace(LayoutName) ? "Unsaved diagnostic layout" : LayoutName,
            sourceAspectRatio = CurrentAspectRatio(),
            clockRegion = ClockRegion,
            minimapRegion = MinimapRegion
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream output = File.Create(path);
        encoder.Save(output);
    }

    private void UpdateOverlayGeometry()
    {
        if (_captureState.Width <= 0
            || _captureState.Height <= 0
            || _previewWidth <= 0
            || _previewHeight <= 0)
        {
            ClockOverlay.Hide();
            MinimapOverlay.Hide();
            return;
        }

        UpdateOverlay(ClockOverlay, ClockRegion);
        UpdateOverlay(MinimapOverlay, MinimapRegion);
    }

    private void UpdateOverlay(RegionOverlayViewModel overlay, NormalizedRegion? region)
    {
        if (region is null)
        {
            overlay.Hide();
            return;
        }

        CoordinateRect rect = _coordinateMapper.NormalizedRegionToPreview(
            region,
            SourceSize(),
            PreviewSize());
        overlay.Update(
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            SelectedRegionType == overlay.RegionType);
    }

    private RegionType? HitRegion(double x, double y)
    {
        if (SelectedRegionType is RegionType selected && Contains(selected, x, y))
        {
            return selected;
        }

        if (Contains(RegionType.Clock, x, y))
        {
            return RegionType.Clock;
        }

        return Contains(RegionType.Minimap, x, y) ? RegionType.Minimap : null;
    }

    private bool Contains(RegionType type, double x, double y)
    {
        RegionOverlayViewModel overlay = type == RegionType.Clock ? ClockOverlay : MinimapOverlay;
        return overlay.IsVisible
            && x >= overlay.Left && x <= overlay.Left + overlay.Width
            && y >= overlay.Top && y <= overlay.Top + overlay.Height;
    }

    private ResizeHandle? HitHandle(RegionType type, double x, double y)
    {
        if (SelectedRegionType != type)
        {
            return null;
        }

        RegionOverlayViewModel overlay = type == RegionType.Clock ? ClockOverlay : MinimapOverlay;
        const double radius = 7;
        (ResizeHandle Handle, double X, double Y)[] handles =
        [
            (ResizeHandle.TopLeft, overlay.Left, overlay.Top),
            (ResizeHandle.Top, overlay.Left + overlay.Width / 2, overlay.Top),
            (ResizeHandle.TopRight, overlay.Left + overlay.Width, overlay.Top),
            (ResizeHandle.Right, overlay.Left + overlay.Width, overlay.Top + overlay.Height / 2),
            (ResizeHandle.BottomRight, overlay.Left + overlay.Width, overlay.Top + overlay.Height),
            (ResizeHandle.Bottom, overlay.Left + overlay.Width / 2, overlay.Top + overlay.Height),
            (ResizeHandle.BottomLeft, overlay.Left, overlay.Top + overlay.Height),
            (ResizeHandle.Left, overlay.Left, overlay.Top + overlay.Height / 2)
        ];
        foreach ((ResizeHandle handle, double handleX, double handleY) in handles)
        {
            if (Math.Abs(x - handleX) <= radius && Math.Abs(y - handleY) <= radius)
            {
                return handle;
            }
        }

        return null;
    }

    private bool TryMap(double x, double y, out NormalizedPoint point)
    {
        point = default;
        if (_captureState.Width <= 0 || _previewWidth <= 0 || _previewHeight <= 0)
        {
            return false;
        }

        NormalizedPoint? mapped = _coordinateMapper.PreviewToNormalized(
            new CoordinatePoint(x, y),
            SourceSize(),
            PreviewSize());
        if (mapped is null)
        {
            return false;
        }

        point = mapped.Value;
        return true;
    }

    private bool TryMapClamped(double x, double y, out NormalizedPoint point)
    {
        if (TryMap(x, y, out point))
        {
            return true;
        }

        if (_captureState.Width <= 0 || _previewWidth <= 0 || _previewHeight <= 0)
        {
            point = default;
            return false;
        }

        PreviewViewport viewport = _coordinateMapper.CalculateViewport(SourceSize(), PreviewSize());
        return TryMap(
            Math.Clamp(x, viewport.X, viewport.X + viewport.Width),
            Math.Clamp(y, viewport.Y, viewport.Y + viewport.Height),
            out point);
    }

    private CoordinateSize SourceSize() => new(_captureState.Width, _captureState.Height);
    private CoordinateSize PreviewSize() => new(_previewWidth, _previewHeight);
    private double? CurrentAspectRatio() => _captureState.Width > 0 && _captureState.Height > 0
        ? (double)_captureState.Width / _captureState.Height
        : null;

    private void RaiseRegionState()
    {
        UpdateOverlayGeometry();
        OnPropertyChanged(nameof(ClockRegion));
        OnPropertyChanged(nameof(MinimapRegion));
        OnPropertyChanged(nameof(ClockCoordinates));
        OnPropertyChanged(nameof(MinimapCoordinates));
        OnPropertyChanged(nameof(SelectedRegionType));
        OnPropertyChanged(nameof(EditMode));
        OnPropertyChanged(nameof(BothRegionsValid));
        OnPropertyChanged(nameof(RegionValidity));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(UnsavedStatus));
        ClearClockCommand.RaiseCanExecuteChanged();
        ClearMinimapCommand.RaiseCanExecuteChanged();
        SaveLayoutCommand.RaiseCanExecuteChanged();
    }

    private static string FormatRegion(NormalizedRegion? region) => region is null
        ? "Not configured"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"x={region.X:F4} y={region.Y:F4} w={region.Width:F4} h={region.Height:F4}");

    private static Int32Rect ToPixelRect(NormalizedRegion region, int width, int height)
    {
        int left = Math.Clamp((int)Math.Floor(region.X * width), 0, width - 1);
        int top = Math.Clamp((int)Math.Floor(region.Y * height), 0, height - 1);
        int right = Math.Clamp((int)Math.Ceiling((region.X + region.Width) * width), left + 1, width);
        int bottom = Math.Clamp((int)Math.Ceiling((region.Y + region.Height) * height), top + 1, height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private static WriteableBitmap CreateBitmap(int width, int height) =>
        new(width, height, 96, 96, PixelFormats.Bgra32, null);

    private void SetCommandError(Exception exception)
    {
        RunOnDispatcher(() =>
        {
            _diagnosticMessage = exception.Message;
            OnPropertyChanged(nameof(DiagnosticMessage));
        });
    }

    private void RaiseCommandStates()
    {
        SelectWindowCommand.RaiseCanExecuteChanged();
        StopCaptureCommand.RaiseCanExecuteChanged();
        SaveDiagnosticFrameCommand.RaiseCanExecuteChanged();
        EditClockCommand.RaiseCanExecuteChanged();
        EditMinimapCommand.RaiseCanExecuteChanged();
        ClearClockCommand.RaiseCanExecuteChanged();
        ClearMinimapCommand.RaiseCanExecuteChanged();
    }

    private void RunOnDispatcher(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
