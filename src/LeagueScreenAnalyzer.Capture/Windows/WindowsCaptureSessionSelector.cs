using LeagueScreenAnalyzer.Capture.Live;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace LeagueScreenAnalyzer.Capture.Windows;

public sealed class WindowsCaptureSessionSelector : ICaptureSessionSelector
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WindowsCaptureSessionSelector> _logger;

    public WindowsCaptureSessionSelector(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<WindowsCaptureSessionSelector>();
    }

    public async Task<CaptureSelectionResult> SelectWindowAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            _logger.LogWarning("Windows graphics capture is not supported on this device.");
            return CaptureSelectionResult.PermissionUnavailable(
                "Windows graphics capture is unavailable. Windows 10 version 1903 or later and compatible graphics hardware are required.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        GraphicsCapturePicker picker = new();
        InitializeWithWindow.Initialize(picker, ownerWindowHandle);
        GraphicsCaptureItem? item = await picker.PickSingleItemAsync();
        cancellationToken.ThrowIfCancellationRequested();

        if (item is null)
        {
            return CaptureSelectionResult.Cancelled();
        }

        return CaptureSelectionResult.Selected(new WindowsCaptureSession(
            item,
            _loggerFactory.CreateLogger<WindowsCaptureSession>()));
    }
}
