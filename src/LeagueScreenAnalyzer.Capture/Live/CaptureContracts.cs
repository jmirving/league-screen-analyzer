using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Capture.Live;

public enum CaptureStatus
{
    Idle,
    Selecting,
    Capturing,
    Stopped,
    Error
}

public enum CaptureSelectionStatus
{
    Selected,
    Cancelled,
    PermissionUnavailable
}

public enum CaptureSessionEndReason
{
    None,
    Stopped,
    SourceClosed,
    InvalidFrameSize,
    Failure
}

public sealed record CaptureSelectionResult(
    CaptureSelectionStatus Status,
    ILiveCaptureSession? Session = null,
    string? ErrorMessage = null)
{
    public static CaptureSelectionResult Selected(ILiveCaptureSession session) =>
        new(CaptureSelectionStatus.Selected, session ?? throw new ArgumentNullException(nameof(session)));

    public static CaptureSelectionResult Cancelled() =>
        new(CaptureSelectionStatus.Cancelled);

    public static CaptureSelectionResult PermissionUnavailable(string? message = null) =>
        new(CaptureSelectionStatus.PermissionUnavailable, ErrorMessage: message);
}

public sealed record CaptureState(
    CaptureStatus Status,
    string? SourceName = null,
    int Width = 0,
    int Height = 0,
    long? LatestSequence = null,
    TimeSpan? LatestTimestamp = null,
    string? ErrorMessage = null)
{
    public bool IsCapturing => Status == CaptureStatus.Capturing;

    public bool CanSelect => Status is CaptureStatus.Idle or CaptureStatus.Stopped or CaptureStatus.Error;
}

public interface ICaptureSessionSelector
{
    Task<CaptureSelectionResult> SelectWindowAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default);
}

public interface ILiveCaptureSession : IFrameSource, IAsyncDisposable
{
    string SourceName { get; }

    CaptureSessionEndReason EndReason { get; }

    string? EndErrorMessage { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class Bgra32FramePayload : IClockImagePayload, IDisposable
{
    private readonly Action? _release;
    private int _disposed;

    public Bgra32FramePayload(Memory<byte> pixels, int stride, Action? release = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        Pixels = pixels;
        Stride = stride;
        _release = release;
    }

    public Memory<byte> Pixels { get; private set; }

    ReadOnlyMemory<byte> IClockImagePayload.BgraPixels => Pixels;

    public int Stride { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pixels = Memory<byte>.Empty;
        _release?.Invoke();
    }
}

public sealed class CaptureFrameEventArgs(SourceFrame frame) : EventArgs
{
    public SourceFrame Frame { get; } = frame ?? throw new ArgumentNullException(nameof(frame));
}

public sealed class CaptureStateChangedEventArgs(CaptureState state) : EventArgs
{
    public CaptureState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}
