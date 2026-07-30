using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
using LeagueScreenAnalyzer.Capture.Processing;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Core.Regions;
using LeagueScreenAnalyzer.Imaging;
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
    private ClockProfileCatalog _clockProfileCatalog;
    private MinimapProfileCatalog _minimapProfileCatalog;
    private ClockProfileCatalog? _pendingClockProfileCatalog;
    private MinimapProfileCatalog? _pendingMinimapProfileCatalog;
    private bool _clockSelectionIsExplicit;
    private bool _minimapSelectionIsExplicit;
    private CaptureState _captureState;
    private WriteableBitmap? _previewImage;
    private readonly CropBitmapCache _clockCropCache = new();
    private readonly CropBitmapCache _minimapCropCache = new();
    private string? _diagnosticMessage;
    private string? _compatibilityWarning;
    private string? _layoutValidationMessage;
    private string _layoutName = string.Empty;
    private string? _selectedSavedLayout;
    private string? _selectedLayoutId;
    private string? _selectedLayoutPersistedName;
    private string? _pendingOverwriteName;
    private readonly ClockRecognitionWorker _clockWorker;
    private readonly ClockDiagnosticWriter _clockDiagnosticWriter = new();
    private readonly MinimapValidationWorker _minimapWorker;
    private readonly MinimapDiagnosticWriter _minimapDiagnosticWriter = new();
    private readonly ObservationPolicy _observationPolicy = new();
    private readonly Dictionary<long, (MapValidationResult Result, MapImage Image)> _mapEvidence = [];
    private readonly Dictionary<long, ClockReading> _clockEvidence = [];
    private MapImage? _latestMinimapImage;
    private MapValidationResult? _latestMapResult;
    private SessionDatasetRecorder? _sessionRecorder;
    private string _minimapStatus = MapFrameStatus.NotConfigured.ToString();
    private string _minimapConfidence = "0.00";
    private string _minimapFeatures = "No minimap features.";
    private string _minimapReason = "Validation has not run.";
    private bool _minimapValidationEnabled = true;
    private string _selectedMinimapProfileId = string.Empty;
    private string? _minimapProfileWarning;
    private MinimapSampleLabel _selectedMinimapLabel = MinimapSampleLabel.Valid;
    private bool _saveUnlabeledMinimapSample;
    private SessionMode _sessionMode = SessionMode.ReplayContinuous;
    private int _observationCadenceMilliseconds = 1000;
    private string _recordingStatus = "Stopped";
    private int _validObservationCount;
    private int _unavailableObservationCount;
    private int _savedMapFrameCount;
    private string? _sessionOutputPath;
    private string? _sessionWarning;
    private int _gapCount;
    private string _firstAcceptedGameTime = "None";
    private string _lastAcceptedGameTime = "None";
    private string _achievedGameTimeResolution = "Not yet measured";
    private ClockRecognitionObservation? _latestClockObservation;
    private bool _recognitionEnabled = true;
    private double _selectedPlaybackSpeed = 1;
    private string _selectedClockProfileId = string.Empty;
    private string? _clockProfileWarning;
    private string _clockStatus = ClockReadingStatus.NotConfigured.ToString();
    private string? _clockRecognizedText;
    private string? _clockAcceptedTime;
    private string? _clockHistoricalTime;
    private string? _clockLastAcceptedSourceTime;
    private string _clockConfidence = "0.00";
    private string _recognitionCadence = "0.0 samples/sec";
    private string? _clockDiagnostic;
    private string _actualClockValue = string.Empty;
    private string? _clockLabelValidationMessage;
    private bool _saveUnlabeledClockSample;
    private RegionType? _activeEditType;
    private RegionEditSession? _precisionSession;
    private BitmapSource? _precisionFrozenImage;
    private Int32Rect _precisionContext;
    private string _pixelX = string.Empty;
    private string _pixelY = string.Empty;
    private string _pixelWidth = string.Empty;
    private string _pixelHeight = string.Empty;
    private string _pixelSize = string.Empty;
    private string? _pixelValidationMessage;
    private bool _updatingPixelFields;
    private PixelRegion? _precisionPointerOriginal;
    private Point _precisionPointerStart;
    private bool _precisionPointerResize;
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
        ISourceAspectRatioCompatibility? aspectRatioCompatibility = null,
        ClockProfileCatalog? clockProfileCatalog = null,
        MinimapProfileCatalog? minimapProfileCatalog = null)
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
        _clockProfileCatalog = clockProfileCatalog ?? ClockProfileCatalog.CreateDefault();
        _minimapProfileCatalog =
            minimapProfileCatalog ?? MinimapProfileCatalog.CreateDefault();
        _selectedClockProfileId = _clockProfileCatalog.DefaultProfile.Id;
        _selectedMinimapProfileId = _minimapProfileCatalog.DefaultProfile.Id;
        foreach (ClockProfileCatalogError error in _clockProfileCatalog.Errors)
        {
            _logger.LogError(
                "Clock profile discovery error at {ManifestPath}: {Message}",
                error.ManifestPath,
                error.Message);
        }
        foreach (MinimapProfileCatalogError error in _minimapProfileCatalog.Errors)
        {
            _logger.LogError(
                "Minimap profile discovery error at {ProfilePath}: {Message}",
                error.ProfilePath,
                error.Message);
        }
        _captureState = captureController.State;
        _clockWorker = new ClockRecognitionWorker(
            new ConstrainedClockImageRecognizer(),
            new ClockTemporalValidator(),
            SelectedProfile());
        _clockWorker.ObservationAvailable += OnClockObservationAvailable;
        _clockWorker.RecognitionFailed += OnClockRecognitionFailed;
        _minimapWorker = new MinimapValidationWorker(
            new StructuralMinimapValidator(SelectedMinimapProfile()));
        _minimapWorker.ObservationAvailable += OnMinimapObservationAvailable;
        _minimapWorker.ValidationFailed += OnMinimapValidationFailed;

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
        SaveClockSampleCommand = new RelayCommand(
            SaveClockSample, () => _latestClockObservation is not null);
        SaveMinimapSampleCommand = new AsyncRelayCommand(
            SaveMinimapSampleAsync,
            () => _latestMinimapImage is not null && _latestMapResult is not null,
            SetCommandError);
        SaveMinimapDiagnosticCommand = new AsyncRelayCommand(
            SaveUnlabeledMinimapDiagnosticAsync,
            () => _latestMinimapImage is not null && _latestMapResult is not null,
            SetCommandError);
        StartSessionRecordingCommand = new AsyncRelayCommand(
            StartSessionRecordingAsync,
            CanStartSessionRecording,
            SetCommandError);
        StopSessionRecordingCommand = new AsyncRelayCommand(
            StopSessionRecordingAsync,
            () => _sessionRecorder is not null,
            SetCommandError);
        OpenSessionFolderCommand = new RelayCommand(
            OpenSessionFolder,
            () => Directory.Exists(_sessionOutputPath));
        EditClockCommand = new RelayCommand(
            () => ActivateEditor(RegionType.Clock), () => CanEditRegions);
        EditMinimapCommand = new RelayCommand(
            () => ActivateEditor(RegionType.Minimap), () => CanEditRegions);
        ClearClockCommand = new RelayCommand(
            () => ClearRegion(RegionType.Clock), () => CanEditRegions && ClockRegion is not null);
        ClearMinimapCommand = new RelayCommand(
            () => ClearRegion(RegionType.Minimap), () => CanEditRegions && MinimapRegion is not null);
        SaveLayoutCommand = new AsyncRelayCommand(
            SaveLayoutAsync, () => BothRegionsValid && _selectedLayoutId is not null, SetCommandError);
        SaveLayoutAsCommand = new AsyncRelayCommand(
            SaveLayoutAsAsync, () => BothRegionsValid && !string.IsNullOrWhiteSpace(LayoutName), SetCommandError);
        ConfirmOverwriteCommand = new AsyncRelayCommand(
            ConfirmOverwriteAsync, () => _pendingOverwriteName is not null, SetCommandError);
        CancelOverwriteCommand = new RelayCommand(CancelOverwrite);
        ResetToSavedCommand = new RelayCommand(ResetToSaved, () => _selectedLayoutId is not null);
        ApplyRegionEditCommand = new RelayCommand(ApplyPrecisionEdit, () => IsPrecisionEditing);
        CancelRegionEditCommand = new RelayCommand(CancelPrecisionEdit, () => IsPrecisionEditing);
        UndoCurrentEditCommand = new RelayCommand(UndoPrecisionEdit, () => IsPrecisionEditing);
        LoadLayoutCommand = new AsyncRelayCommand(
            LoadLayoutAsync, () => !string.IsNullOrWhiteSpace(SelectedSavedLayout), SetCommandError);
        DeleteLayoutCommand = new AsyncRelayCommand(
            DeleteLayoutAsync, () => !string.IsNullOrWhiteSpace(SelectedSavedLayout), SetCommandError);

        _captureController.StateChanged += OnCaptureStateChanged;
        _captureController.FrameArrived += OnFrameArrived;
        _ = RefreshLayoutsAsync();
    }

    public string Title => "League Screen Analyzer";
    public string MilestoneDescription => "Precision-first timestamped minimap observations";
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
    public bool BothRegionsValid =>
        _regionEditor.Validate(RegionType.Clock)?.IsValid == true &&
        _regionEditor.Validate(RegionType.Minimap)?.IsValid == true;
    public string RegionValidity => BothRegionsValid
        ? "Both required regions are valid."
        : string.Join(
            " ",
            new[]
            {
                ClockRegion is null
                    ? "Configure CLOCK."
                    : _regionEditor.Validate(RegionType.Clock)?.Error,
                MinimapRegion is null
                    ? "Configure MINIMAP."
                    : _regionEditor.Validate(RegionType.Minimap)?.Error
            }.OfType<string>());
    public bool HasUnsavedChanges => _regionEditor.HasUnsavedChanges;
    public string UnsavedStatus => HasUnsavedChanges ? "Unsaved changes" : "Saved";
    public string LoadedLayout => _selectedLayoutPersistedName ?? "None";
    public NormalizedRegion? ClockRegion => _regionEditor.GetRegion(RegionType.Clock);
    public NormalizedRegion? MinimapRegion => _regionEditor.GetRegion(RegionType.Minimap);
    public string ClockCoordinates => FormatRegion(ClockRegion);
    public string MinimapCoordinates => FormatRegion(MinimapRegion);
    public RegionType? SelectedRegionType => _regionEditor.SelectedRegionType;
    public string EditMode => _activeEditType is RegionType type
        ? $"Editing {type.ToString().ToUpperInvariant()}"
        : "Editing off";
    public bool IsPrecisionEditing => _precisionSession is not null;
    public int PrecisionEditorZIndex => IsPrecisionEditing ? 100 : -1;
    public bool IsClockPrecisionEdit =>
        _precisionSession?.SelectedRegionType == RegionType.Clock;
    public bool IsMinimapPrecisionEdit =>
        _precisionSession?.SelectedRegionType == RegionType.Minimap;
    public ImageSource? PrecisionFrozenImage => _precisionFrozenImage;
    public double PrecisionContextWidth =>
        _precisionContext.Width * (_precisionSession?.Zoom ?? 1);
    public double PrecisionContextHeight =>
        _precisionContext.Height * (_precisionSession?.Zoom ?? 1);
    public double PrecisionOverlayLeft =>
        _precisionSession is null
            ? 0
            : (_precisionSession.PixelGeometry.X - _precisionContext.X) * _precisionSession.Zoom;
    public double PrecisionOverlayTop =>
        _precisionSession is null
            ? 0
            : (_precisionSession.PixelGeometry.Y - _precisionContext.Y) * _precisionSession.Zoom;
    public double PrecisionOverlayWidth =>
        (_precisionSession?.PixelGeometry.Width ?? 0) * (_precisionSession?.Zoom ?? 1);
    public double PrecisionOverlayHeight =>
        (_precisionSession?.PixelGeometry.Height ?? 0) * (_precisionSession?.Zoom ?? 1);
    public Brush PrecisionBorderBrush => IsClockPrecisionEdit ? Brushes.Gold : Brushes.DeepSkyBlue;
    public string PrecisionEditorTitle =>
        _precisionSession is null
            ? "Precision region editor"
            : $"Edit {_precisionSession.SelectedRegionType.ToString().ToUpperInvariant()}";
    public string PrecisionZoom =>
        _precisionSession is null ? string.Empty : $"{_precisionSession.Zoom * 100:0}%";
    public string PixelRatio => _precisionSession is null
        ? "—"
        : $"{_precisionSession.Validation.PixelAspectRatio:0.###}:1";
    public string PixelGeometryStatus
    {
        get
        {
            if (_precisionSession is null)
            {
                return "Not editing.";
            }

            RegionGeometryValidation validation = _precisionSession.Validation;
            PixelRegion pixels = _precisionSession.PixelGeometry;
            string shape = _precisionSession.SelectedRegionType == RegionType.Clock
                ? $"{validation.PixelAspectRatio:0.###}:1"
                : $"{pixels.Width} × {pixels.Height}";
            return validation.IsValid
                ? $"{_precisionSession.SelectedRegionType.ToString().ToUpperInvariant()} geometry: Valid — {shape}"
                : $"{_precisionSession.SelectedRegionType.ToString().ToUpperInvariant()} geometry: Invalid — {validation.Error}";
        }
    }
    public string PixelValidationMessage => _pixelValidationMessage ?? string.Empty;
    public string PixelX
    {
        get => _pixelX;
        set => SetPixelField(ref _pixelX, value, nameof(PixelX));
    }
    public string PixelY
    {
        get => _pixelY;
        set => SetPixelField(ref _pixelY, value, nameof(PixelY));
    }
    public string PixelWidth
    {
        get => _pixelWidth;
        set => SetPixelField(ref _pixelWidth, value, nameof(PixelWidth));
    }
    public string PixelHeight
    {
        get => _pixelHeight;
        set => SetPixelField(ref _pixelHeight, value, nameof(PixelHeight));
    }
    public string PixelSize
    {
        get => _pixelSize;
        set => SetPixelField(ref _pixelSize, value, nameof(PixelSize));
    }

    public string LayoutName
    {
        get => _layoutName;
        set
        {
            if (Set(ref _layoutName, value))
            {
                SaveLayoutAsCommand.RaiseCanExecuteChanged();
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

    public bool IsOverwriteConfirmationVisible => _pendingOverwriteName is not null;
    public string OverwriteWarning => _pendingOverwriteName is null
        ? string.Empty
        : $"A layout named '{_pendingOverwriteName}' already exists. Confirming replaces that target only; the loaded layout is unchanged.";
    public IReadOnlyList<ClockProfileCatalogEntry> AvailableClockProfiles =>
        _clockProfileCatalog.Profiles;

    public void RefreshProfileCatalogs(
        ClockProfileCatalog clockCatalog,
        MinimapProfileCatalog minimapCatalog)
    {
        ArgumentNullException.ThrowIfNull(clockCatalog);
        ArgumentNullException.ThrowIfNull(minimapCatalog);
        if (_captureState.IsCapturing)
        {
            _pendingClockProfileCatalog = clockCatalog;
            _pendingMinimapProfileCatalog = minimapCatalog;
            return;
        }

        ApplyProfileCatalogRefresh(clockCatalog, minimapCatalog);
    }

    public IReadOnlyList<double> PlaybackSpeeds { get; } = [0.25, 0.5, 1, 2, 4, 8];
    public bool CanConfigureClockRecognition => !_captureState.IsCapturing;
    public bool RecognitionEnabled
    {
        get => _recognitionEnabled;
        set
        {
            if (Set(ref _recognitionEnabled, value))
            {
                _logger.LogInformation("Clock recognition {State}.", value ? "enabled" : "disabled");
                if (!value)
                {
                    _ = _clockWorker.StopAsync();
                    SetClockUnavailable(ClockReadingStatus.NotConfigured, "Recognition is disabled.");
                }
            }
        }
    }

    public string SelectedClockProfileId
    {
        get => _selectedClockProfileId;
        set
        {
            if (!CanConfigureClockRecognition ||
                string.Equals(_selectedClockProfileId, value, StringComparison.Ordinal))
            {
                return;
            }

            if (!_clockProfileCatalog.TryGet(value, out _))
            {
                ClockProfileCatalogEntry? replacement =
                    _clockProfileCatalog.SuggestReplacement(value);
                _clockProfileWarning = MissingProfileWarning(
                    "Clock",
                    value,
                    replacement?.Id);
                OnPropertyChanged(nameof(ClockProfileWarning));
                return;
            }

            _selectedClockProfileId = value;
            _clockSelectionIsExplicit = true;
            OnPropertyChanged();
            ApplyClockSettings();
            _clockProfileWarning = null;
            _logger.LogInformation("Clock profile selected: {ProfileId}.", value);
            OnPropertyChanged(nameof(SelectedClockProfileName));
            OnPropertyChanged(nameof(SelectedClockProfileTemplateCount));
            OnPropertyChanged(nameof(ClockProfileWarning));
            OnPropertyChanged(nameof(ActiveClockProfileId));
        }
    }

    public string SelectedClockProfileName => SelectedCatalogEntry().DisplayName;
    public int SelectedClockProfileTemplateCount => SelectedCatalogEntry().TemplateCount;
    public string ActiveClockProfileId => _clockWorker.Profile.Id;
    public string ClockProfileWarning => _clockProfileWarning ?? string.Empty;

    public double SelectedPlaybackSpeed
    {
        get => _selectedPlaybackSpeed;
        set
        {
            if (!CanConfigureClockRecognition || !PlaybackSpeeds.Contains(value) ||
                !Set(ref _selectedPlaybackSpeed, value))
            {
                return;
            }

            ApplyClockSettings();
            _logger.LogInformation("Clock playback speed selected: {PlaybackSpeed}x.", value);
            OnPropertyChanged(nameof(PlaybackSpeedDisplay));
        }
    }

    public string PlaybackSpeedDisplay => $"{SelectedPlaybackSpeed:0.##}x";
    public string ClockStatus => _clockStatus;
    public string ClockRecognizedText => _clockRecognizedText ?? "Unavailable";
    public string ClockAcceptedTime => _clockAcceptedTime ?? "Unavailable";
    public string ClockHistoricalTime => _clockHistoricalTime ?? "None";
    public string ClockLastAcceptedSourceTime => _clockLastAcceptedSourceTime ?? "None";
    public string ClockConfidence => _clockConfidence;
    public string RecognitionCadence => _recognitionCadence;
    public string ClockDiagnostic => _clockDiagnostic ?? "No recognition observation.";
    public string ActualClockValue
    {
        get => _actualClockValue;
        set
        {
            if (Set(ref _actualClockValue, value))
            {
                ValidateClockLabel(showBlankMessage: false);
            }
        }
    }

    public string? ClockLabelValidationMessage => _clockLabelValidationMessage;

    public bool SaveUnlabeledClockSample
    {
        get => _saveUnlabeledClockSample;
        set
        {
            if (Set(ref _saveUnlabeledClockSample, value))
            {
                ValidateClockLabel(showBlankMessage: false);
            }
        }
    }
    public IReadOnlyList<MinimapProfileCatalogEntry> AvailableMinimapProfiles =>
        _minimapProfileCatalog.Profiles;
    public string MinimapProfileId
    {
        get => _selectedMinimapProfileId;
        set
        {
            if (!CanConfigureMinimap ||
                string.Equals(_selectedMinimapProfileId, value, StringComparison.Ordinal))
            {
                return;
            }

            if (!_minimapProfileCatalog.TryGet(value, out MinimapProfileCatalogEntry? entry))
            {
                MinimapProfileCatalogEntry? replacement =
                    _minimapProfileCatalog.SuggestReplacement(value);
                _minimapProfileWarning = MissingProfileWarning(
                    "Minimap",
                    value,
                    replacement?.Id);
                _logger.LogError("Unavailable minimap profile selected: {ProfileId}.", value);
                OnPropertyChanged(nameof(MinimapProfileWarning));
                return;
            }

            _minimapWorker.SetValidator(new StructuralMinimapValidator(entry!.Profile));
            _selectedMinimapProfileId = value;
            _minimapSelectionIsExplicit = true;
            _minimapProfileWarning = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MinimapProfileName));
            OnPropertyChanged(nameof(MinimapProfileVersion));
            OnPropertyChanged(nameof(MinimapCalibrationStatus));
            OnPropertyChanged(nameof(MinimapProfileSource));
            OnPropertyChanged(nameof(ActiveMinimapProfileId));
            OnPropertyChanged(nameof(MinimapProfileWarning));
            _logger.LogInformation(
                "Minimap profile selected: {ProfileId} from {SourcePath}.",
                value,
                entry.SourcePath ?? "built-in");
        }
    }
    public string MinimapProfileName => SelectedMinimapEntry().DisplayName;
    public int MinimapProfileVersion => SelectedMinimapEntry().Version;
    public string MinimapCalibrationStatus => SelectedMinimapEntry().CalibrationStatus;
    public string MinimapProfileSource =>
        SelectedMinimapEntry().SourcePath ?? "Built into application";
    public string ActiveMinimapProfileId =>
        _minimapWorker.ActiveProfileId ?? "Unavailable";
    public string MinimapProfileWarning
    {
        get
        {
            if (_minimapProfileWarning is not null)
            {
                return _minimapProfileWarning;
            }

            return _minimapProfileCatalog.Errors.Count == 0
                ? string.Empty
                : string.Join(
                    " | ",
                    _minimapProfileCatalog.Errors.Select(error => error.Message));
        }
    }
    public bool CanConfigureMinimap =>
        !_captureState.IsCapturing && _sessionRecorder is null;
    public bool MinimapValidationEnabled
    {
        get => _minimapValidationEnabled;
        set
        {
            if (!CanConfigureMinimap || !Set(ref _minimapValidationEnabled, value))
            {
                return;
            }

            if (!value)
            {
                _ = _minimapWorker.StopAsync();
                SetMinimapUnavailable(MapFrameStatus.NotConfigured, "Minimap validation is disabled.");
            }
        }
    }
    public string MinimapStatus => _minimapStatus;
    public string MinimapConfidence => _minimapConfidence;
    public string MinimapFeatures => _minimapFeatures;
    public string MinimapReason => _minimapReason;
    public IReadOnlyList<MinimapSampleLabel> MinimapLabels { get; } =
        [MinimapSampleLabel.Valid, MinimapSampleLabel.Invalid, MinimapSampleLabel.Uncertain];
    public MinimapSampleLabel SelectedMinimapLabel
    {
        get => _selectedMinimapLabel;
        set => Set(ref _selectedMinimapLabel, value);
    }
    public bool SaveUnlabeledMinimapSample
    {
        get => _saveUnlabeledMinimapSample;
        set => Set(ref _saveUnlabeledMinimapSample, value);
    }
    public IReadOnlyList<SessionMode> SessionModes { get; } =
        [SessionMode.ReplayContinuous, SessionMode.BroadcastVod];
    public SessionMode SelectedSessionMode
    {
        get => _sessionMode;
        set
        {
            if (CanConfigureMinimap)
            {
                Set(ref _sessionMode, value);
            }
        }
    }
    public IReadOnlyList<int> ObservationCadencesMilliseconds { get; } = [250, 500, 1000, 2000];
    public int ObservationCadenceMilliseconds
    {
        get => _observationCadenceMilliseconds;
        set
        {
            if (CanConfigureMinimap && ObservationCadencesMilliseconds.Contains(value))
            {
                Set(ref _observationCadenceMilliseconds, value);
            }
        }
    }
    public string RecordingStatus => _recordingStatus;
    public int ValidObservationCount => _validObservationCount;
    public int UnavailableObservationCount => _unavailableObservationCount;
    public int SavedMapFrameCount => _savedMapFrameCount;
    public int GapCount => _gapCount;
    public string FirstAcceptedGameTime => _firstAcceptedGameTime;
    public string LastAcceptedGameTime => _lastAcceptedGameTime;
    public string AchievedGameTimeResolution => _achievedGameTimeResolution;
    public string CurrentAcceptedGameTime => _clockAcceptedTime ?? "Unavailable";
    public string SessionOutputPath => _sessionOutputPath ?? "No active session.";
    public string SessionWarning => _sessionWarning ??
        (SelectedMinimapProfile().CalibratedForCanonicalRecording
            ? string.Empty
            : "Initial minimap profile is calibration-oriented; recordings are experimental.");
    public ObservableCollection<string> AvailableSavedLayouts { get; } = [];
    public RegionOverlayViewModel ClockOverlay { get; }
    public RegionOverlayViewModel MinimapOverlay { get; }
    public CropPreviewViewModel ClockCrop { get; }
    public CropPreviewViewModel MinimapCrop { get; }
    public AsyncRelayCommand SelectWindowCommand { get; }
    public AsyncRelayCommand StopCaptureCommand { get; }
    public RelayCommand SaveDiagnosticFrameCommand { get; }
    public RelayCommand SaveClockSampleCommand { get; }
    public AsyncRelayCommand SaveMinimapSampleCommand { get; }
    public AsyncRelayCommand SaveMinimapDiagnosticCommand { get; }
    public AsyncRelayCommand StartSessionRecordingCommand { get; }
    public AsyncRelayCommand StopSessionRecordingCommand { get; }
    public RelayCommand OpenSessionFolderCommand { get; }
    public RelayCommand EditClockCommand { get; }
    public RelayCommand EditMinimapCommand { get; }
    public RelayCommand ClearClockCommand { get; }
    public RelayCommand ClearMinimapCommand { get; }
    public AsyncRelayCommand SaveLayoutCommand { get; }
    public AsyncRelayCommand SaveLayoutAsCommand { get; }
    public AsyncRelayCommand ConfirmOverwriteCommand { get; }
    public RelayCommand CancelOverwriteCommand { get; }
    public RelayCommand ResetToSavedCommand { get; }
    public RelayCommand ApplyRegionEditCommand { get; }
    public RelayCommand CancelRegionEditCommand { get; }
    public RelayCommand UndoCurrentEditCommand { get; }
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
        if (result.Operation == RegionEditOperation.Create &&
            result.After is NormalizedRegion created)
        {
            OpenPrecisionEditor(result.RegionType, created);
        }
        RaiseRegionState();
    }

    public void CancelEdit()
    {
        if (IsPrecisionEditing)
        {
            CancelPrecisionEdit();
            return;
        }

        _regionEditor.Cancel();
        RaiseRegionState();
    }

    public bool NudgePrecisionRegion(int deltaX, int deltaY, bool resize)
    {
        if (_precisionSession is null)
        {
            return false;
        }

        PixelGeometryUpdate update = resize
            ? _precisionSession.ResizeByPixels(deltaX, deltaY)
            : _precisionSession.MoveByPixels(deltaX, deltaY);
        UpdateAfterPrecisionChange(update);
        return true;
    }

    public bool PrecisionPointerDown(double x, double y)
    {
        if (_precisionSession is null)
        {
            return false;
        }

        PixelRegion region = _precisionSession.PixelGeometry;
        double sourceX = (x / _precisionSession.Zoom) + _precisionContext.X;
        double sourceY = (y / _precisionSession.Zoom) + _precisionContext.Y;
        const double hitPadding = 3;
        bool nearRight = Math.Abs(sourceX - region.Right) <= hitPadding;
        bool nearBottom = Math.Abs(sourceY - region.Bottom) <= hitPadding;
        _precisionPointerResize = nearRight && nearBottom;
        if (!_precisionPointerResize &&
            (sourceX < region.X || sourceX > region.Right ||
             sourceY < region.Y || sourceY > region.Bottom))
        {
            return false;
        }

        _precisionPointerStart = new Point(sourceX, sourceY);
        _precisionPointerOriginal = region;
        return true;
    }

    public void PrecisionPointerMove(double x, double y)
    {
        if (_precisionSession is null || _precisionPointerOriginal is not PixelRegion original)
        {
            return;
        }

        int dx = (int)Math.Round(
            ((x / _precisionSession.Zoom) + _precisionContext.X) - _precisionPointerStart.X);
        int dy = (int)Math.Round(
            ((y / _precisionSession.Zoom) + _precisionContext.Y) - _precisionPointerStart.Y);
        PixelGeometryUpdate update;
        if (_precisionPointerResize)
        {
            if (_precisionSession.SelectedRegionType == RegionType.Minimap)
            {
                int size = Math.Max(1, original.Width + (Math.Abs(dx) >= Math.Abs(dy) ? dx : dy));
                update = _precisionSession.SetPixelGeometry(original.X, original.Y, size);
            }
            else
            {
                int width = Math.Max(1, original.Width + dx);
                int height = Math.Max(1, original.Height + dy);
                if (width < height * 2)
                {
                    height = Math.Max(1, width / 2);
                }
                update = _precisionSession.SetPixelGeometry(
                    original.X,
                    original.Y,
                    width,
                    height);
            }
        }
        else
        {
            int sourceWidth = (int)Math.Round(_regionEditor.SourceSize.Width);
            int sourceHeight = (int)Math.Round(_regionEditor.SourceSize.Height);
            update = _precisionSession.SetPixelGeometry(
                Math.Clamp(original.X + dx, 0, sourceWidth - original.Width),
                Math.Clamp(original.Y + dy, 0, sourceHeight - original.Height),
                original.Width,
                original.Height);
        }

        UpdateAfterPrecisionChange(update);
    }

    public void PrecisionPointerUp()
    {
        _precisionPointerOriginal = null;
        _precisionPointerResize = false;
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
        _clockWorker.ObservationAvailable -= OnClockObservationAvailable;
        _clockWorker.RecognitionFailed -= OnClockRecognitionFailed;
        _minimapWorker.ObservationAvailable -= OnMinimapObservationAvailable;
        _minimapWorker.ValidationFailed -= OnMinimapValidationFailed;
        await _minimapWorker.DisposeAsync();
        if (_sessionRecorder is not null)
        {
            await _sessionRecorder.DisposeAsync();
            _sessionRecorder = null;
        }
        DisposeMapEvidence();
        _latestMinimapImage?.Dispose();
        await _clockWorker.DisposeAsync();
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
        if (_regionEditor.GetRegion(type) is NormalizedRegion region)
        {
            OpenPrecisionEditor(type, region);
        }
        RaiseRegionState();
    }

    private void OpenPrecisionEditor(RegionType type, NormalizedRegion region)
    {
        _regionEditor.Cancel();
        _precisionSession = new RegionEditSession(
            type,
            region,
            _regionEditor.SourceSize);
        PixelRegion pixels = _precisionSession.PixelGeometry;
        int sourceWidth = (int)Math.Round(_regionEditor.SourceSize.Width);
        int sourceHeight = (int)Math.Round(_regionEditor.SourceSize.Height);
        int horizontalContext = type == RegionType.Clock ? 24 : 40;
        int verticalContext = type == RegionType.Clock ? 18 : 40;
        int left = Math.Max(0, pixels.X - horizontalContext);
        int top = Math.Max(0, pixels.Y - verticalContext);
        int right = Math.Min(sourceWidth, pixels.Right + horizontalContext);
        int bottom = Math.Min(sourceHeight, pixels.Bottom + verticalContext);
        _precisionContext = new Int32Rect(left, top, right - left, bottom - top);
        if (_previewImage is not null)
        {
            WriteableBitmap frozen = new(_previewImage);
            frozen.Freeze();
            CroppedBitmap cropped = new(frozen, _precisionContext);
            cropped.Freeze();
            _precisionFrozenImage = cropped;
        }

        RefreshPixelFields();
        RaisePrecisionState();
    }

    private void ApplyPrecisionEdit()
    {
        if (_precisionSession is null)
        {
            return;
        }

        RegionType type = _precisionSession.SelectedRegionType;
        _regionEditor.SetWorkingRegion(type, _precisionSession.Apply());
        ClosePrecisionEditor();
        RaiseRegionState();
    }

    private void CancelPrecisionEdit()
    {
        if (_precisionSession is null)
        {
            return;
        }

        _precisionSession.Cancel();
        ClosePrecisionEditor();
        RaiseRegionState();
    }

    private void UndoPrecisionEdit()
    {
        if (_precisionSession is null)
        {
            return;
        }

        NormalizedRegion original = _precisionSession.Cancel();
        RegionType type = _precisionSession.SelectedRegionType;
        _precisionSession = new RegionEditSession(
            type,
            original,
            _regionEditor.SourceSize,
            zoom: _precisionSession.Zoom);
        RefreshPixelFields();
        RaisePrecisionState();
    }

    private void ClosePrecisionEditor()
    {
        _precisionSession = null;
        _precisionFrozenImage = null;
        _precisionPointerOriginal = null;
        _pixelValidationMessage = null;
        RaisePrecisionState();
    }

    private void ResetToSaved()
    {
        CancelPrecisionEdit();
        _regionEditor.ResetToSaved();
        _activeEditType = null;
        _layoutValidationMessage = $"Restored loaded layout '{LoadedLayout}'.";
        OnPropertyChanged(nameof(LayoutValidationMessage));
        RaiseRegionState();
    }

    private void SetPixelField(ref string field, string value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (!_updatingPixelFields)
        {
            CommitPixelFields();
        }
    }

    private void CommitPixelFields()
    {
        if (_precisionSession is null)
        {
            return;
        }

        if (!int.TryParse(_pixelX, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(_pixelY, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            SetPixelError("X and Y must be whole source-pixel numbers.");
            return;
        }

        PixelGeometryUpdate update;
        if (_precisionSession.SelectedRegionType == RegionType.Minimap)
        {
            if (!int.TryParse(_pixelSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
            {
                SetPixelError("Size must be a whole source-pixel number.");
                return;
            }
            update = _precisionSession.SetPixelGeometry(x, y, size);
        }
        else
        {
            if (!int.TryParse(_pixelWidth, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(_pixelHeight, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
            {
                SetPixelError("Width and Height must be whole source-pixel numbers.");
                return;
            }
            update = _precisionSession.SetPixelGeometry(x, y, width, height);
        }

        UpdateAfterPrecisionChange(update);
    }

    private void UpdateAfterPrecisionChange(PixelGeometryUpdate update)
    {
        _pixelValidationMessage = update.Error;
        if (update.IsValid)
        {
            RefreshPixelFields();
            UpdateOverlayGeometry();
        }
        RaisePrecisionState();
    }

    private void SetPixelError(string error)
    {
        _pixelValidationMessage = error;
        OnPropertyChanged(nameof(PixelValidationMessage));
    }

    private void RefreshPixelFields()
    {
        if (_precisionSession is null)
        {
            return;
        }

        PixelRegion pixels = _precisionSession.PixelGeometry;
        _updatingPixelFields = true;
        _pixelX = pixels.X.ToString(CultureInfo.InvariantCulture);
        _pixelY = pixels.Y.ToString(CultureInfo.InvariantCulture);
        _pixelWidth = pixels.Width.ToString(CultureInfo.InvariantCulture);
        _pixelHeight = pixels.Height.ToString(CultureInfo.InvariantCulture);
        _pixelSize = pixels.Width.ToString(CultureInfo.InvariantCulture);
        _updatingPixelFields = false;
        OnPropertyChanged(nameof(PixelX));
        OnPropertyChanged(nameof(PixelY));
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
        OnPropertyChanged(nameof(PixelSize));
    }

    private void RaisePrecisionState()
    {
        OnPropertyChanged(nameof(IsPrecisionEditing));
        OnPropertyChanged(nameof(PrecisionEditorZIndex));
        OnPropertyChanged(nameof(IsClockPrecisionEdit));
        OnPropertyChanged(nameof(IsMinimapPrecisionEdit));
        OnPropertyChanged(nameof(PrecisionFrozenImage));
        OnPropertyChanged(nameof(PrecisionContextWidth));
        OnPropertyChanged(nameof(PrecisionContextHeight));
        OnPropertyChanged(nameof(PrecisionOverlayLeft));
        OnPropertyChanged(nameof(PrecisionOverlayTop));
        OnPropertyChanged(nameof(PrecisionOverlayWidth));
        OnPropertyChanged(nameof(PrecisionOverlayHeight));
        OnPropertyChanged(nameof(PrecisionBorderBrush));
        OnPropertyChanged(nameof(PrecisionEditorTitle));
        OnPropertyChanged(nameof(PrecisionZoom));
        OnPropertyChanged(nameof(PixelRatio));
        OnPropertyChanged(nameof(PixelGeometryStatus));
        OnPropertyChanged(nameof(PixelValidationMessage));
        ApplyRegionEditCommand.RaiseCanExecuteChanged();
        CancelRegionEditCommand.RaiseCanExecuteChanged();
        UndoCurrentEditCommand.RaiseCanExecuteChanged();
    }

    private void RaiseOverwriteState()
    {
        OnPropertyChanged(nameof(IsOverwriteConfirmationVisible));
        OnPropertyChanged(nameof(OverwriteWarning));
        ConfirmOverwriteCommand.RaiseCanExecuteChanged();
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
        if (_selectedLayoutId is null)
        {
            return;
        }

        await PersistLayoutAsync(_selectedLayoutId, overwrite: true, selectSavedLayout: true);
    }

    private async Task SaveLayoutAsAsync()
    {
        string target = LayoutName.Trim();
        if (string.Equals(target, _selectedLayoutId, StringComparison.OrdinalIgnoreCase))
        {
            _layoutValidationMessage =
                $"'{target}' is the loaded layout. Use Save to update it, or enter a different name for Save As.";
            OnPropertyChanged(nameof(LayoutValidationMessage));
            return;
        }

        if (AvailableSavedLayouts.Any(
                name => string.Equals(name, target, StringComparison.OrdinalIgnoreCase)))
        {
            _pendingOverwriteName = target;
            RaiseOverwriteState();
            return;
        }

        await PersistLayoutAsync(target, overwrite: false, selectSavedLayout: true);
    }

    private async Task ConfirmOverwriteAsync()
    {
        if (_pendingOverwriteName is not string target)
        {
            return;
        }

        await PersistLayoutAsync(target, overwrite: true, selectSavedLayout: true);
        _pendingOverwriteName = null;
        RaiseOverwriteState();
    }

    private void CancelOverwrite()
    {
        _pendingOverwriteName = null;
        RaiseOverwriteState();
        _layoutValidationMessage = "Save As overwrite canceled; no layout was changed.";
        OnPropertyChanged(nameof(LayoutValidationMessage));
    }

    private async Task PersistLayoutAsync(string targetName, bool overwrite, bool selectSavedLayout)
    {
        if (ClockRegion is not NormalizedRegion clock || MinimapRegion is not NormalizedRegion minimap)
        {
            return;
        }

        double? aspect = CurrentAspectRatio();
        CaptureLayout layout = new(
            targetName,
            clock,
            minimap,
            aspect,
            SelectedClockProfileId);
        await _layoutStore.SaveAsync(layout, overwrite);
        _regionEditor.MarkSaved();
        _layoutAspectRatio = aspect;
        if (selectSavedLayout)
        {
            _selectedLayoutId = layout.Name;
            _selectedLayoutPersistedName = layout.Name;
            SelectedSavedLayout = layout.Name;
            LayoutName = layout.Name;
            OnPropertyChanged(nameof(LoadedLayout));
        }
        _layoutValidationMessage = $"Saved layout '{layout.Name}'.";
        _logger.LogInformation(
            "Capture layout {LayoutName} saved with overwrite={Overwrite}.",
            layout.Name,
            overwrite);
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
            IReadOnlyList<string> geometryWarnings =
                _regionEditor.Load(layout.ClockRegion, layout.MinimapRegion);
            _layoutAspectRatio = layout.SourceAspectRatio;
            if (layout.ClockProfileId is string profileId)
            {
                RestorePersistedClockProfile(profileId, layout.Name);
            }
            LayoutName = layout.Name;
            _selectedLayoutId = name;
            _selectedLayoutPersistedName = layout.Name;
            OnPropertyChanged(nameof(LoadedLayout));
            _activeEditType = null;
            _layoutValidationMessage = geometryWarnings.Count == 0
                ? $"Loaded layout '{layout.Name}'."
                : $"Loaded layout '{layout.Name}'. {string.Join(" ", geometryWarnings)}";
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
        if (string.Equals(_selectedLayoutId, name, StringComparison.OrdinalIgnoreCase))
        {
            _selectedLayoutId = null;
            _selectedLayoutPersistedName = null;
            OnPropertyChanged(nameof(LoadedLayout));
        }
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
                if (_regionEditor.Operation == RegionEditOperation.None)
                {
                    IReadOnlyList<string> geometryWarnings = _regionEditor.SetSourceSize(
                        new RegionSourceSize(args.State.Width, args.State.Height));
                    if (geometryWarnings.Count > 0)
                    {
                        _layoutValidationMessage = string.Join(" ", geometryWarnings);
                        OnPropertyChanged(nameof(LayoutValidationMessage));
                        RaiseRegionState();
                    }
                }
                EvaluateCompatibility();
                UpdateOverlayGeometry();
            }

            OnPropertyChanged(nameof(CaptureStatus));
            OnPropertyChanged(nameof(SelectedSourceName));
            OnPropertyChanged(nameof(FrameDimensions));
            OnPropertyChanged(nameof(LatestFrame));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(CanEditRegions));
            OnPropertyChanged(nameof(CanConfigureClockRecognition));
            OnPropertyChanged(nameof(CanConfigureMinimap));
            OnPropertyChanged(nameof(ActiveClockProfileId));
            OnPropertyChanged(nameof(ActiveMinimapProfileId));
            if (!args.State.IsCapturing)
            {
                _ = _clockWorker.StopAsync();
                _ = _minimapWorker.StopAsync();
                if (_sessionRecorder is not null)
                {
                    _ = StopSessionRecordingAsync();
                }
                ApplyPendingProfileCatalogRefresh();
            }
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

        RunOnDispatcher(() => UpdatePreview(args.Frame, payload));
    }

    private unsafe void UpdatePreview(SourceFrame frame, Bgra32FramePayload payload)
    {
        int width = frame.Width;
        int height = frame.Height;
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
            UpdateCrop(
                RegionType.Clock,
                DisplayRegion(RegionType.Clock),
                width,
                height,
                payload,
                pixelPointer,
                frame.SequenceNumber,
                frame.SourceTimestamp);
            UpdateCrop(
                RegionType.Minimap,
                DisplayRegion(RegionType.Minimap),
                width,
                height,
                payload,
                pixelPointer,
                frame.SequenceNumber,
                frame.SourceTimestamp);
        }

        SaveDiagnosticFrameCommand.RaiseCanExecuteChanged();
    }

    private unsafe void UpdateCrop(
        RegionType type,
        NormalizedRegion? region,
        int sourceWidth,
        int sourceHeight,
        Bgra32FramePayload payload,
        byte* pixelPointer,
        long sourceSequence = 0,
        TimeSpan sourceTimestamp = default)
    {
        CropPreviewViewModel preview = type == RegionType.Clock ? ClockCrop : MinimapCrop;
        if (region is null)
        {
            preview.Image = null;
            preview.Dimensions = "Not configured";
            preview.Coordinates = "Not configured";
            if (type == RegionType.Clock)
            {
                _ = _clockWorker.StopAsync();
                SetClockUnavailable(ClockReadingStatus.NotConfigured, "CLOCK region is not configured.");
            }
            else
            {
                _ = _minimapWorker.StopAsync();
                SetMinimapUnavailable(MapFrameStatus.NotConfigured, "MINIMAP region is not configured.");
            }
            return;
        }

        Int32Rect pixelRect = ToPixelRect(region, sourceWidth, sourceHeight);
        RegionGeometryValidation? geometry = _regionEditor.Validate(type);
        if (geometry is { IsValid: false })
        {
            if (type == RegionType.Clock)
            {
                _ = _clockWorker.StopAsync();
                SetClockUnavailable(ClockReadingStatus.NotConfigured, geometry.Error!);
            }
            else
            {
                _ = _minimapWorker.StopAsync();
                SetMinimapUnavailable(MapFrameStatus.IncompatibleGeometry, geometry.Error!);
            }
        }
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

        if (geometry?.IsValid != false &&
            type == RegionType.Clock && RecognitionEnabled && _captureState.IsCapturing)
        {
            try
            {
                if (!_clockWorker.IsRunning)
                {
                    ApplyClockSettings();
                    _clockWorker.Start();
                    _logger.LogInformation(
                        "Clock recognition started with profile {ProfileId} at {PlaybackSpeed}x.",
                        SelectedClockProfileId,
                        SelectedPlaybackSpeed);
                }

                int cropStride = pixelRect.Width * 4;
                byte[] copy = new byte[cropStride * pixelRect.Height];
                for (int y = 0; y < pixelRect.Height; y++)
                {
                    new ReadOnlySpan<byte>(
                        pixelPointer + offset + (y * payload.Stride),
                        cropStride).CopyTo(copy.AsSpan(y * cropStride, cropStride));
                }

                _clockWorker.TrySubmit(new ClockImage(
                    pixelRect.Width,
                    pixelRect.Height,
                    cropStride,
                    copy,
                    sourceSequence,
                    sourceTimestamp));
            }
            catch (Exception exception)
            {
                ReportClockRecognitionFailure(exception);
            }
        }
        else if (geometry?.IsValid != false &&
                 type == RegionType.Minimap &&
                 MinimapValidationEnabled &&
                 _captureState.IsCapturing)
        {
            int cropStride = pixelRect.Width * 4;
            byte[] copy = new byte[cropStride * pixelRect.Height];
            for (int y = 0; y < pixelRect.Height; y++)
            {
                new ReadOnlySpan<byte>(
                    pixelPointer + offset + (y * payload.Stride),
                    cropStride).CopyTo(copy.AsSpan(y * cropStride, cropStride));
            }

            if (!_minimapWorker.IsRunning)
            {
                _minimapWorker.Start();
            }

            _minimapWorker.TrySubmit(new MapImage(
                pixelRect.Width,
                pixelRect.Height,
                cropStride,
                copy,
                sourceSequence,
                sourceTimestamp));
        }
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

    private void SaveClockSample()
    {
        if (_latestClockObservation is null)
        {
            return;
        }

        ClockSampleLabel? label = null;
        if (_saveUnlabeledClockSample)
        {
            if (!string.IsNullOrWhiteSpace(_actualClockValue))
            {
                SetClockLabelValidation(
                    "Clear Actual clock value before saving an unlabeled diagnostic sample.");
                return;
            }
        }
        else if (!ClockSampleLabelParser.TryParse(
                     _actualClockValue,
                     out label,
                     out string? validationMessage))
        {
            SetClockLabelValidation(validationMessage);
            _diagnosticMessage = $"Clock sample not saved: {validationMessage}";
            OnPropertyChanged(nameof(DiagnosticMessage));
            return;
        }

        try
        {
            string root = Path.Combine(Environment.CurrentDirectory, "artifacts", "clock-samples");
            string directory = _clockDiagnosticWriter.Write(
                root,
                _latestClockObservation,
                _clockWorker.Profile,
                label,
                _saveUnlabeledClockSample);
            bool labeled = label is not null;
            _diagnosticMessage = labeled
                ? $"Saved labeled clock sample ({label!.Value}): {directory}"
                : $"Saved unlabeled clock diagnostic sample: {directory}";
            _logger.LogInformation(
                "Clock {SampleKind} sample written to {Path} with status {Status}.",
                labeled ? "labeled" : "unlabeled diagnostic",
                directory,
                _latestClockObservation.Reading.Status);
            if (labeled)
            {
                _actualClockValue = string.Empty;
                OnPropertyChanged(nameof(ActualClockValue));
            }

            _saveUnlabeledClockSample = false;
            OnPropertyChanged(nameof(SaveUnlabeledClockSample));
            SetClockLabelValidation(null);
        }
        catch (Exception exception)
        {
            _diagnosticMessage = $"Could not save clock sample: {exception.Message}";
            _logger.LogError(exception, "Failed to save clock diagnostic sample.");
        }

        OnPropertyChanged(nameof(DiagnosticMessage));
    }

    private void ValidateClockLabel(bool showBlankMessage)
    {
        if (_saveUnlabeledClockSample)
        {
            SetClockLabelValidation(string.IsNullOrWhiteSpace(_actualClockValue)
                ? "The next save will be an unlabeled diagnostic sample."
                : "Clear Actual clock value before saving an unlabeled diagnostic sample.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_actualClockValue) && !showBlankMessage)
        {
            SetClockLabelValidation(null);
            return;
        }

        SetClockLabelValidation(
            ClockSampleLabelParser.TryParse(_actualClockValue, out _, out string? message)
                ? null
                : message);
    }

    private void SetClockLabelValidation(string? message)
    {
        if (_clockLabelValidationMessage == message)
        {
            return;
        }

        _clockLabelValidationMessage = message;
        OnPropertyChanged(nameof(ClockLabelValidationMessage));
    }

    private void OnClockObservationAvailable(object? sender, ClockRecognitionObservation observation)
    {
        RunOnDispatcher(() =>
        {
            _latestClockObservation = observation;
            ClockReading reading = observation.Reading;
            if (reading.SourceFrameSequence is long clockSequence)
            {
                _clockEvidence[clockSequence] = reading;
                TrimEvidence();
                TryRecordSameFrame(clockSequence);
            }
            _clockStatus = reading.Status.ToString();
            _clockRecognizedText = reading.RawRecognizedText;
            _clockAcceptedTime = reading.Status == ClockReadingStatus.Valid
                ? reading.GameTime?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                : null;
            _clockHistoricalTime = reading.LastAcceptedGameTime?.ToString(
                @"hh\:mm\:ss",
                CultureInfo.InvariantCulture);
            _clockLastAcceptedSourceTime = reading.LastAcceptedSourceTimestamp?.ToString(
                @"hh\:mm\:ss\.fff",
                CultureInfo.InvariantCulture);
            _clockConfidence = reading.Confidence.ToString("0.00", CultureInfo.InvariantCulture);
            _recognitionCadence = observation.ActualSamplesPerSecond.ToString(
                "0.0 'samples/sec'",
                CultureInfo.InvariantCulture);
            _clockDiagnostic = reading.DiagnosticReason ?? "Candidate accepted.";
            OnPropertyChanged(nameof(ClockStatus));
            OnPropertyChanged(nameof(ClockRecognizedText));
            OnPropertyChanged(nameof(ClockAcceptedTime));
            OnPropertyChanged(nameof(ClockHistoricalTime));
            OnPropertyChanged(nameof(ClockLastAcceptedSourceTime));
            OnPropertyChanged(nameof(ClockConfidence));
            OnPropertyChanged(nameof(RecognitionCadence));
            OnPropertyChanged(nameof(ClockDiagnostic));
            OnPropertyChanged(nameof(CurrentAcceptedGameTime));
            SaveClockSampleCommand.RaiseCanExecuteChanged();

            if (reading.Status == ClockReadingStatus.Valid)
            {
                _logger.LogDebug(
                    "Clock candidate accepted: {Text}, confidence {Confidence}.",
                    reading.RawRecognizedText,
                    reading.Confidence);
            }
            else
            {
                _logger.LogWarning(
                    "Clock candidate rejected with {Status}: {Reason}",
                    reading.Status,
                    reading.DiagnosticReason);
            }
        });
    }

    private void OnMinimapObservationAvailable(
        object? sender,
        MinimapValidationObservation observation)
    {
        byte[] pixels = observation.Image.BgraPixels.ToArray();
        MapImage retained = new(
            observation.Image.Width,
            observation.Image.Height,
            observation.Image.Width * 4,
            pixels,
            observation.Image.SourceFrameSequence,
            observation.Image.SourceTimestamp);
        RunOnDispatcher(() =>
        {
            _latestMinimapImage?.Dispose();
            _latestMinimapImage = CloneMapImage(retained);
            _latestMapResult = observation.Result;
            _minimapStatus = observation.Result.Status.ToString();
            _minimapConfidence = observation.Result.Confidence.ToString("0.00", CultureInfo.InvariantCulture);
            _minimapReason = observation.Result.Reasons.Count == 0
                ? "Required profile checks passed."
                : string.Join(" ", observation.Result.Reasons);
            _minimapFeatures = FormatMapFeatures(observation.Result.Features);
            if (_mapEvidence.Remove(retained.SourceFrameSequence, out var replaced))
            {
                replaced.Image.Dispose();
            }

            _mapEvidence[retained.SourceFrameSequence] = (observation.Result, retained);
            TrimEvidence();
            TryRecordSameFrame(retained.SourceFrameSequence);
            OnPropertyChanged(nameof(MinimapStatus));
            OnPropertyChanged(nameof(MinimapConfidence));
            OnPropertyChanged(nameof(MinimapReason));
            OnPropertyChanged(nameof(MinimapFeatures));
            SaveMinimapSampleCommand.RaiseCanExecuteChanged();
            SaveMinimapDiagnosticCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnMinimapValidationFailed(
        object? sender,
        MinimapValidationFailedEventArgs args)
    {
        RunOnDispatcher(() =>
        {
            _logger.LogError(args.Exception, "Minimap validation failed.");
            SetMinimapUnavailable(
                MapFrameStatus.Unknown,
                $"Minimap validation failed: {args.Exception.Message}");
        });
    }

    private void TryRecordSameFrame(long sequence)
    {
        if (_sessionRecorder is null ||
            !_clockEvidence.Remove(sequence, out ClockReading? clock) ||
            !_mapEvidence.Remove(sequence, out var map))
        {
            return;
        }

        try
        {
            SourceFrame source = new(
                sequence,
                map.Image.SourceTimestamp,
                _captureState.Width,
                _captureState.Height,
                EvidencePayload.Instance);
            RegionFrame region = new(
                RegionType.Minimap,
                sequence,
                map.Image.SourceTimestamp,
                map.Image.Width,
                map.Image.Height,
                EvidencePayload.Instance);
            TimelineObservation timeline = _observationPolicy.Create(
                source,
                region,
                clock,
                map.Result,
                SelectedSessionMode);
            MapImage? image = timeline.Status == ObservationStatus.Valid
                ? CloneMapImage(map.Image)
                : null;
            _sessionRecorder.Record(timeline, image);
            if (timeline.Status == ObservationStatus.Valid)
            {
                _validObservationCount++;
            }
            else
            {
                _unavailableObservationCount++;
            }

            _savedMapFrameCount = _sessionRecorder.SavedMapFrameCount;
            OnPropertyChanged(nameof(ValidObservationCount));
            OnPropertyChanged(nameof(UnavailableObservationCount));
            OnPropertyChanged(nameof(SavedMapFrameCount));
        }
        catch (Exception exception)
        {
            _sessionWarning = $"Session observation failed: {exception.Message}";
            _logger.LogError(exception, "Minimap session observation processing failed.");
            OnPropertyChanged(nameof(SessionWarning));
        }
        finally
        {
            map.Image.Dispose();
        }
    }

    private async Task SaveMinimapSampleAsync()
    {
        await SaveMinimapDiagnosticCoreAsync(
            SaveUnlabeledMinimapSample ? MinimapSampleLabel.Unlabeled : SelectedMinimapLabel);
        _saveUnlabeledMinimapSample = false;
        OnPropertyChanged(nameof(SaveUnlabeledMinimapSample));
    }

    private Task SaveUnlabeledMinimapDiagnosticAsync() =>
        SaveMinimapDiagnosticCoreAsync(MinimapSampleLabel.Unlabeled);

    private async Task SaveMinimapDiagnosticCoreAsync(MinimapSampleLabel label)
    {
        if (_latestMinimapImage is null || _latestMapResult is null)
        {
            return;
        }

        string root = Path.Combine(Environment.CurrentDirectory, "artifacts", "minimap-samples");
        string directory = await _minimapDiagnosticWriter.WriteAsync(
            root,
            _latestMinimapImage,
            SelectedMinimapProfile(),
            _latestMapResult,
            label,
            _latestClockObservation?.Reading.Status == ClockReadingStatus.Valid
                ? _latestClockObservation.Reading.GameTime
                : null,
            SelectedSavedLayout ?? LayoutName);
        _diagnosticMessage = $"Saved {label.ToString().ToLowerInvariant()} minimap diagnostic: {directory}";
        OnPropertyChanged(nameof(DiagnosticMessage));
    }

    private bool CanStartSessionRecording() =>
        _sessionRecorder is null &&
        _captureState.IsCapturing &&
        ClockRegion is not null &&
        MinimapRegion is not null &&
        RecognitionEnabled &&
        MinimapValidationEnabled &&
        !string.IsNullOrWhiteSpace(SelectedClockProfileId) &&
        !string.IsNullOrWhiteSpace(MinimapProfileId);

    private Task StartSessionRecordingAsync()
    {
        if (!CanStartSessionRecording())
        {
            throw new InvalidOperationException(
                "Recording requires active capture, CLOCK and MINIMAP regions, clock recognition, and valid profiles.");
        }

        string root = Path.Combine(Environment.CurrentDirectory, "artifacts", "sessions");
        _sessionRecorder = new SessionDatasetRecorder(
            root,
            new SessionRecordingConfiguration(
                SelectedSessionMode,
                TimeSpan.FromMilliseconds(ObservationCadenceMilliseconds),
                SelectedSavedLayout ?? (string.IsNullOrWhiteSpace(LayoutName) ? "active-layout" : LayoutName),
                SelectedClockProfileId,
                MinimapProfileId,
                SelectedPlaybackSpeed,
                _captureState.Width,
                _captureState.Height),
            typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString());
        _sessionOutputPath = _sessionRecorder.OutputDirectory;
        MinimapValidationProfile minimapProfile = SelectedMinimapProfile();
        _recordingStatus = minimapProfile.CalibratedForCanonicalRecording
            ? "Recording"
            : "Recording (experimental profile)";
        _validObservationCount = 0;
        _unavailableObservationCount = 0;
        _savedMapFrameCount = 0;
        _gapCount = 0;
        _firstAcceptedGameTime = "None";
        _lastAcceptedGameTime = "None";
        _achievedGameTimeResolution = "Not yet measured";
        _sessionWarning = minimapProfile.CalibratedForCanonicalRecording
            ? null
            : "Canonical accuracy is not claimed until explicitly labeled real samples calibrate this profile.";
        RaiseSessionState();
        return Task.CompletedTask;
    }

    private async Task StopSessionRecordingAsync()
    {
        SessionDatasetRecorder? recorder = _sessionRecorder;
        if (recorder is null)
        {
            return;
        }

        SessionRecordingSummary summary = await recorder.StopAsync();
        _savedMapFrameCount = summary.SavedMapFrames;
        _gapCount = summary.GapCount;
        _firstAcceptedGameTime = FormatOptionalGameTime(summary.FirstAcceptedGameTime);
        _lastAcceptedGameTime = FormatOptionalGameTime(summary.LastAcceptedGameTime);
        _achievedGameTimeResolution = summary.AchievedGameTimeResolution is TimeSpan achieved
            ? $"{achieved.TotalMilliseconds:0} ms"
            : "Insufficient saved anchors";
        _sessionWarning = summary.Warning ?? _sessionWarning;
        _recordingStatus = "Stopped";
        _sessionRecorder = null;
        RaiseSessionState();
    }

    private void OpenSessionFolder()
    {
        if (!Directory.Exists(_sessionOutputPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", _sessionOutputPath)
        {
            UseShellExecute = true
        });
    }

    private void RaiseSessionState()
    {
        OnPropertyChanged(nameof(RecordingStatus));
        OnPropertyChanged(nameof(SavedMapFrameCount));
        OnPropertyChanged(nameof(GapCount));
        OnPropertyChanged(nameof(FirstAcceptedGameTime));
        OnPropertyChanged(nameof(LastAcceptedGameTime));
        OnPropertyChanged(nameof(AchievedGameTimeResolution));
        OnPropertyChanged(nameof(SessionOutputPath));
        OnPropertyChanged(nameof(SessionWarning));
        OnPropertyChanged(nameof(CanConfigureMinimap));
        StartSessionRecordingCommand.RaiseCanExecuteChanged();
        StopSessionRecordingCommand.RaiseCanExecuteChanged();
        OpenSessionFolderCommand.RaiseCanExecuteChanged();
    }

    private void SetMinimapUnavailable(MapFrameStatus status, string reason)
    {
        _minimapStatus = status.ToString();
        _minimapConfidence = "0.00";
        _minimapFeatures = "No trusted feature result.";
        _minimapReason = reason;
        OnPropertyChanged(nameof(MinimapStatus));
        OnPropertyChanged(nameof(MinimapConfidence));
        OnPropertyChanged(nameof(MinimapFeatures));
        OnPropertyChanged(nameof(MinimapReason));
    }

    private void TrimEvidence()
    {
        while (_mapEvidence.Count > 16)
        {
            long key = _mapEvidence.Keys.Min();
            if (_mapEvidence.Remove(key, out var removed))
            {
                removed.Image.Dispose();
            }
        }

        while (_clockEvidence.Count > 16)
        {
            _clockEvidence.Remove(_clockEvidence.Keys.Min());
        }
    }

    private void DisposeMapEvidence()
    {
        foreach (var evidence in _mapEvidence.Values)
        {
            evidence.Image.Dispose();
        }

        _mapEvidence.Clear();
        _clockEvidence.Clear();
    }

    private static MapImage CloneMapImage(MapImage image) =>
        new(
            image.Width,
            image.Height,
            image.Width * 4,
            image.BgraPixels.ToArray(),
            image.SourceFrameSequence,
            image.SourceTimestamp);

    private static string FormatMapFeatures(MapFeatureValues? features) =>
        features is null
            ? "Required features unavailable."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{features.CropWidth}x{features.CropHeight}; mean {features.MeanLuminance:F1}; " +
                $"variance {features.LuminanceVariance:F1}; edges {features.EdgeDensity:P1}; " +
                $"black {features.NearBlackPercentage:P1}; border {features.BorderConsistency:F2}; " +
                $"corners {features.CornerConsistency:F2}");

    private static string FormatOptionalGameTime(TimeSpan? gameTime) =>
        gameTime?.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) ?? "None";

    private sealed class EvidencePayload : IFramePayload
    {
        public static EvidencePayload Instance { get; } = new();
    }

    private void OnClockRecognitionFailed(object? sender, ClockRecognitionFailedEventArgs args)
    {
        RunOnDispatcher(() => ReportClockRecognitionFailure(args.Exception));
    }

    private void ReportClockRecognitionFailure(Exception exception)
    {
        _logger.LogError(exception, "Clock recognition failed.");
        SetClockUnavailable(
            ClockReadingStatus.Unreadable,
            $"Clock recognition failed: {exception.Message}");
    }

    private void ApplyClockSettings()
    {
        ClockRecognitionProfile profile =
            _clockProfileCatalog.Get(SelectedClockProfileId).Profile
                .WithPlaybackSpeed(SelectedPlaybackSpeed);
        _clockWorker.SetProfile(profile);
        OnPropertyChanged(nameof(ActiveClockProfileId));
    }

    private void ApplyPendingProfileCatalogRefresh()
    {
        if (_pendingClockProfileCatalog is null ||
            _pendingMinimapProfileCatalog is null)
        {
            return;
        }

        ClockProfileCatalog clockCatalog = _pendingClockProfileCatalog;
        MinimapProfileCatalog minimapCatalog = _pendingMinimapProfileCatalog;
        _pendingClockProfileCatalog = null;
        _pendingMinimapProfileCatalog = null;
        ApplyProfileCatalogRefresh(clockCatalog, minimapCatalog);
    }

    private void ApplyProfileCatalogRefresh(
        ClockProfileCatalog clockCatalog,
        MinimapProfileCatalog minimapCatalog)
    {
        if (!_clockSelectionIsExplicit)
        {
            _clockProfileCatalog = clockCatalog;
            _selectedClockProfileId = clockCatalog.DefaultProfile.Id;
            _clockProfileWarning = null;
            ApplyClockSettings();
            RaiseClockProfileProperties();
        }
        else if (clockCatalog.TryGet(_selectedClockProfileId, out _))
        {
            _clockProfileCatalog = clockCatalog;
            _clockProfileWarning = null;
            ApplyClockSettings();
            RaiseClockProfileProperties();
        }
        else
        {
            ClockProfileCatalogEntry? replacement =
                clockCatalog.SuggestReplacement(_selectedClockProfileId);
            _clockProfileWarning = MissingProfileWarning(
                "Selected clock",
                _selectedClockProfileId,
                replacement?.Id);
            OnPropertyChanged(nameof(ClockProfileWarning));
        }

        if (!_minimapSelectionIsExplicit)
        {
            _minimapProfileCatalog = minimapCatalog;
            _selectedMinimapProfileId = minimapCatalog.DefaultProfile.Id;
            _minimapProfileWarning = null;
            _minimapWorker.SetValidator(new StructuralMinimapValidator(
                minimapCatalog.DefaultProfile.Profile));
            RaiseMinimapProfileProperties();
        }
        else if (minimapCatalog.TryGet(
                     _selectedMinimapProfileId,
                     out MinimapProfileCatalogEntry? selected))
        {
            _minimapProfileCatalog = minimapCatalog;
            _minimapProfileWarning = null;
            _minimapWorker.SetValidator(new StructuralMinimapValidator(
                selected!.Profile));
            RaiseMinimapProfileProperties();
        }
        else
        {
            MinimapProfileCatalogEntry? replacement =
                minimapCatalog.SuggestReplacement(_selectedMinimapProfileId);
            _minimapProfileWarning = MissingProfileWarning(
                "Selected minimap",
                _selectedMinimapProfileId,
                replacement?.Id);
            OnPropertyChanged(nameof(MinimapProfileWarning));
        }
    }

    private void RaiseClockProfileProperties()
    {
        OnPropertyChanged(nameof(AvailableClockProfiles));
        OnPropertyChanged(nameof(SelectedClockProfileId));
        OnPropertyChanged(nameof(SelectedClockProfileName));
        OnPropertyChanged(nameof(SelectedClockProfileTemplateCount));
        OnPropertyChanged(nameof(ActiveClockProfileId));
        OnPropertyChanged(nameof(ClockProfileWarning));
    }

    private void RaiseMinimapProfileProperties()
    {
        OnPropertyChanged(nameof(AvailableMinimapProfiles));
        OnPropertyChanged(nameof(MinimapProfileId));
        OnPropertyChanged(nameof(MinimapProfileName));
        OnPropertyChanged(nameof(MinimapProfileVersion));
        OnPropertyChanged(nameof(MinimapCalibrationStatus));
        OnPropertyChanged(nameof(MinimapProfileSource));
        OnPropertyChanged(nameof(ActiveMinimapProfileId));
        OnPropertyChanged(nameof(MinimapProfileWarning));
    }

    private static string MissingProfileWarning(
        string profileKind,
        string missingProfileId,
        string? replacementId) =>
        replacementId is null
            ? $"{profileKind} profile '{missingProfileId}' is unavailable. The current selection was not changed; no compatible replacement is installed."
            : $"{profileKind} profile '{missingProfileId}' is unavailable. The current selection was not changed. Suggested compatible replacement: '{replacementId}'.";

    private ClockRecognitionProfile SelectedProfile() =>
        SelectedCatalogEntry().Profile.WithPlaybackSpeed(_selectedPlaybackSpeed);

    private ClockProfileCatalogEntry SelectedCatalogEntry() =>
        _clockProfileCatalog.Get(_selectedClockProfileId);

    private MinimapProfileCatalogEntry SelectedMinimapEntry() =>
        _minimapProfileCatalog.Get(_selectedMinimapProfileId);

    private MinimapValidationProfile SelectedMinimapProfile() =>
        SelectedMinimapEntry().Profile;

    internal bool RestorePersistedClockProfile(string profileId, string sourceName)
    {
        if (_clockProfileCatalog.TryGet(profileId, out _))
        {
            _clockSelectionIsExplicit = true;
            _clockProfileWarning = null;
            if (!string.Equals(_selectedClockProfileId, profileId, StringComparison.Ordinal))
            {
                _selectedClockProfileId = profileId;
                ApplyClockSettings();
                OnPropertyChanged(nameof(SelectedClockProfileId));
                OnPropertyChanged(nameof(SelectedClockProfileName));
                OnPropertyChanged(nameof(SelectedClockProfileTemplateCount));
            }
            return true;
        }

        ClockProfileCatalogEntry? replacement =
            _clockProfileCatalog.SuggestReplacement(profileId);
        _clockProfileWarning = MissingProfileWarning(
            "Saved clock",
            profileId,
            replacement?.Id);
        _logger.LogWarning(
            "Capture layout {LayoutName} references unavailable clock profile {ProfileId}.",
            sourceName,
            profileId);
        OnPropertyChanged(nameof(ClockProfileWarning));
        return false;
    }

    internal bool RestorePersistedMinimapProfile(string profileId, string sourceName)
    {
        if (_minimapProfileCatalog.TryGet(profileId, out MinimapProfileCatalogEntry? entry))
        {
            _minimapSelectionIsExplicit = true;
            _minimapProfileWarning = null;
            if (!string.Equals(_selectedMinimapProfileId, profileId, StringComparison.Ordinal))
            {
                _minimapWorker.SetValidator(new StructuralMinimapValidator(entry!.Profile));
                _selectedMinimapProfileId = profileId;
                RaiseMinimapProfileProperties();
            }
            return true;
        }

        MinimapProfileCatalogEntry? replacement =
            _minimapProfileCatalog.SuggestReplacement(profileId);
        _minimapProfileWarning = MissingProfileWarning(
            "Saved minimap",
            profileId,
            replacement?.Id);
        _logger.LogWarning(
            "Persisted source {SourceName} references unavailable minimap profile {ProfileId}.",
            sourceName,
            profileId);
        OnPropertyChanged(nameof(MinimapProfileWarning));
        return false;
    }

    private void SetClockUnavailable(ClockReadingStatus status, string reason)
    {
        _clockStatus = status.ToString();
        _clockRecognizedText = null;
        _clockAcceptedTime = null;
        _clockDiagnostic = reason;
        OnPropertyChanged(nameof(ClockStatus));
        OnPropertyChanged(nameof(ClockRecognizedText));
        OnPropertyChanged(nameof(ClockAcceptedTime));
        OnPropertyChanged(nameof(ClockDiagnostic));
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
            clockProfileId = SelectedClockProfileId,
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

        UpdateOverlay(ClockOverlay, DisplayRegion(RegionType.Clock));
        UpdateOverlay(MinimapOverlay, DisplayRegion(RegionType.Minimap));
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
        const double radius = RegionHandleDesign.HitTargetSize / 2;
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
            if (!RegionHandleDesign.For(type).Contains(handle))
            {
                continue;
            }

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
        SaveLayoutAsCommand.RaiseCanExecuteChanged();
        ResetToSavedCommand.RaiseCanExecuteChanged();
        StartSessionRecordingCommand.RaiseCanExecuteChanged();
    }

    private static string FormatRegion(NormalizedRegion? region) => region is null
        ? "Not configured"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"x={region.X:F4} y={region.Y:F4} w={region.Width:F4} h={region.Height:F4}");

    private static Int32Rect ToPixelRect(NormalizedRegion region, int width, int height)
    {
        PixelRegion pixels = PixelRegionGeometry.ToPixels(
            region,
            new RegionSourceSize(width, height));
        return new Int32Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height);
    }

    private NormalizedRegion? DisplayRegion(RegionType type) =>
        _precisionSession?.SelectedRegionType == type
            ? _precisionSession.WorkingRegion
            : _regionEditor.GetRegion(type);

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
        StartSessionRecordingCommand.RaiseCanExecuteChanged();
        StopSessionRecordingCommand.RaiseCanExecuteChanged();
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
