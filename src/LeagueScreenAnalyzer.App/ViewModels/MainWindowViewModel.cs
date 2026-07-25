using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LeagueScreenAnalyzer.App.Services;
using LeagueScreenAnalyzer.Capture.Live;
using Microsoft.Extensions.Logging;

namespace LeagueScreenAnalyzer.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly CaptureController _captureController;
    private readonly IWindowHandleProvider _windowHandleProvider;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private CaptureState _captureState;
    private WriteableBitmap? _previewImage;
    private string? _diagnosticMessage;
    private bool _disposed;

    public MainWindowViewModel(
        CaptureController captureController,
        IWindowHandleProvider windowHandleProvider,
        Dispatcher dispatcher,
        ILogger<MainWindowViewModel> logger)
    {
        _captureController = captureController ?? throw new ArgumentNullException(nameof(captureController));
        _windowHandleProvider = windowHandleProvider ?? throw new ArgumentNullException(nameof(windowHandleProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _captureState = captureController.State;
        SelectWindowCommand = new AsyncRelayCommand(
            SelectWindowAsync,
            () => _captureState.CanSelect,
            SetCommandError);
        StopCaptureCommand = new AsyncRelayCommand(
            () => _captureController.StopAsync(),
            () => _captureState.IsCapturing,
            SetCommandError);
        SaveDiagnosticFrameCommand = new RelayCommand(
            SaveDiagnosticFrame,
            () => _captureState.IsCapturing && _previewImage is not null);
        _captureController.StateChanged += OnCaptureStateChanged;
        _captureController.FrameArrived += OnFrameArrived;
    }

    public string Title => "League Screen Analyzer";

    public string MilestoneDescription => "Selected-window capture preview";

    public string CaptureStatus => _captureState.Status.ToString();

    public string SelectedSourceName => _captureState.SourceName ?? "No window selected";

    public string FrameDimensions => _captureState.Width > 0 && _captureState.Height > 0
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{_captureState.Width} × {_captureState.Height}")
        : "—";

    public string LatestFrame => _captureState.LatestSequence is long sequence
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"Sequence {sequence} · {_captureState.LatestTimestamp:hh\\:mm\\:ss\\.fff}")
        : "—";

    public string? ErrorMessage => _captureState.ErrorMessage;

    public string? DiagnosticMessage => _diagnosticMessage;

    public ImageSource? PreviewImage => _previewImage;

    public AsyncRelayCommand SelectWindowCommand { get; }

    public AsyncRelayCommand StopCaptureCommand { get; }

    public RelayCommand SaveDiagnosticFrameCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    private void OnCaptureStateChanged(object? sender, CaptureStateChangedEventArgs args)
    {
        RunOnDispatcher(() =>
        {
            _captureState = args.State;
            OnPropertyChanged(nameof(CaptureStatus));
            OnPropertyChanged(nameof(SelectedSourceName));
            OnPropertyChanged(nameof(FrameDimensions));
            OnPropertyChanged(nameof(LatestFrame));
            OnPropertyChanged(nameof(ErrorMessage));
            RaiseCommandStates();
        });
    }

    private void OnFrameArrived(object? sender, CaptureFrameEventArgs args)
    {
        if (args.Frame.Payload is not Bgra32FramePayload payload)
        {
            return;
        }

        RunOnDispatcher(() => UpdatePreview(
            args.Frame.Width,
            args.Frame.Height,
            payload));
    }

    private unsafe void UpdatePreview(int width, int height, Bgra32FramePayload payload)
    {
        if (_previewImage is null
            || _previewImage.PixelWidth != width
            || _previewImage.PixelHeight != height)
        {
            _previewImage = new WriteableBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null);
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
        }

        SaveDiagnosticFrameCommand.RaiseCanExecuteChanged();
    }

    private void SaveDiagnosticFrame()
    {
        if (_previewImage is null)
        {
            return;
        }

        try
        {
            string artifactsDirectory = Path.GetFullPath(
                Path.Combine(Environment.CurrentDirectory, "artifacts"));
            Directory.CreateDirectory(artifactsDirectory);
            string fileName = $"capture-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
            string path = Path.Combine(artifactsDirectory, fileName);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(_previewImage));
            using FileStream output = File.Create(path);
            encoder.Save(output);
            _diagnosticMessage = $"Saved diagnostic frame: {path}";
            _logger.LogInformation("Saved diagnostic capture frame to {Path}.", path);
        }
        catch (Exception exception)
        {
            _diagnosticMessage = $"Could not save diagnostic frame: {exception.Message}";
            _logger.LogError(exception, "Failed to save diagnostic capture frame.");
        }

        OnPropertyChanged(nameof(DiagnosticMessage));
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
