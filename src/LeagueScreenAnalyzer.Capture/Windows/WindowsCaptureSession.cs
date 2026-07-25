using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Core.Models;
using Microsoft.Extensions.Logging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace LeagueScreenAnalyzer.Capture.Windows;

public sealed class WindowsCaptureSession : ILiveCaptureSession
{
    private const int FramePoolBufferCount = 2;
    private readonly GraphicsCaptureItem _item;
    private readonly ILogger<WindowsCaptureSession> _logger;
    private readonly LatestFrameQueue<PendingFrame> _frames = new();
    private readonly object _lifecycleGate = new();
    private readonly Stopwatch _sourceClock = new();
    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private long _sequence;
    private int _processingFrame;
    private bool _started;
    private bool _stopped;
    private bool _disposed;
    private SizeInt32 _framePoolSize;

    public WindowsCaptureSession(
        GraphicsCaptureItem item,
        ILogger<WindowsCaptureSession> logger)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SourceName = string.IsNullOrWhiteSpace(item.DisplayName) ? "Selected window" : item.DisplayName;
    }

    public string SourceName { get; }

    public CaptureSessionEndReason EndReason { get; private set; }

    public string? EndErrorMessage { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return Task.CompletedTask;
            }

            ValidateSize(_item.Size);
            _framePoolSize = _item.Size;
            _device = Direct3DDeviceFactory.Create();
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                FramePoolBufferCount,
                _item.Size);
            _captureSession = _framePool.CreateCaptureSession(_item);
            _framePool.FrameArrived += OnFrameArrived;
            _item.Closed += OnItemClosed;
            _captureSession.StartCapture();
            _sourceClock.Start();
            _started = true;
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<SourceFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (PendingFrame pending in _frames.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return pending.TakeFrame();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stop(CaptureSessionEndReason.Stopped);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop(EndReason == CaptureSessionEndReason.None ? CaptureSessionEndReason.Stopped : EndReason);
        await _frames.DisposeAsync().ConfigureAwait(false);
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _processingFrame, 1) != 0)
        {
            frame.Dispose();
            return;
        }

        PendingFrame? pending = null;
        try
        {
            SizeInt32 size;
            bool dimensionsChanged;

            using (frame)
            {
                size = frame.ContentSize;
                if (size.Width <= 0 || size.Height <= 0)
                {
                    _logger.LogError(
                        "Capture returned invalid frame dimensions {Width}x{Height}.",
                        size.Width,
                        size.Height);
                    Stop(
                        CaptureSessionEndReason.InvalidFrameSize,
                        $"Capture returned invalid dimensions {size.Width} × {size.Height}.");
                    return;
                }

                dimensionsChanged = size.Width != _framePoolSize.Width
                    || size.Height != _framePoolSize.Height;
                if (!dimensionsChanged)
                {
                    using SoftwareBitmap bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                        frame.Surface,
                        BitmapAlphaMode.Premultiplied);
                    pending = CopyBitmap(bitmap, size);
                }
            }

            if (dimensionsChanged && _framePool is not null)
            {
                _logger.LogInformation(
                    "Capture dimensions changed to {Width}x{Height}; recreating frame pool.",
                    size.Width,
                    size.Height);
                _framePool.Recreate(
                    _device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    FramePoolBufferCount,
                    size);
                _framePoolSize = size;
                return;
            }

            _frames.TryWrite(pending
                ?? throw new InvalidOperationException("A stable capture frame was not copied."));
            pending = null;
        }
        catch (Exception exception)
        {
            pending?.Dispose();
            _logger.LogError(exception, "Capture failed while copying a preview frame.");
            Stop(CaptureSessionEndReason.Failure, $"Capture frame copy failed: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _processingFrame, 0);
        }
    }

    private unsafe PendingFrame CopyBitmap(SoftwareBitmap bitmap, SizeInt32 size)
    {
        int destinationStride = checked(size.Width * 4);
        int byteCount = checked(destinationStride * size.Height);
        byte[] pixels = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            using BitmapBuffer bitmapBuffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
            using IMemoryBufferReference reference = bitmapBuffer.CreateReference();
            IMemoryBufferByteAccess byteAccess = reference.As<IMemoryBufferByteAccess>();
            byteAccess.GetBuffer(out byte* source, out uint capacity);
            BitmapPlaneDescription plane = bitmapBuffer.GetPlaneDescription(0);
            int requiredSourceBytes = checked(plane.StartIndex + (plane.Stride * size.Height));
            if (requiredSourceBytes > capacity)
            {
                throw new InvalidDataException("The captured bitmap buffer is smaller than its plane description.");
            }

            fixed (byte* destination = pixels)
            {
                for (int row = 0; row < size.Height; row++)
                {
                    Buffer.MemoryCopy(
                        source + plane.StartIndex + (row * plane.Stride),
                        destination + (row * destinationStride),
                        destinationStride,
                        destinationStride);
                }
            }

            long sequence = Interlocked.Increment(ref _sequence) - 1;
            TimeSpan timestamp = _sourceClock.Elapsed;
            Bgra32FramePayload payload = new(
                pixels.AsMemory(0, byteCount),
                destinationStride,
                () => ArrayPool<byte>.Shared.Return(pixels));
            return new PendingFrame(new SourceFrame(
                sequence,
                timestamp,
                size.Width,
                size.Height,
                payload));
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(pixels);
            throw;
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        _logger.LogWarning("Selected window {SourceName} closed.", SourceName);
        Stop(CaptureSessionEndReason.SourceClosed, "The selected window closed.");
    }

    private void Stop(CaptureSessionEndReason reason, string? errorMessage = null)
    {
        lock (_lifecycleGate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            EndReason = reason;
            EndErrorMessage = errorMessage;

            _item.Closed -= OnItemClosed;
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }

            _captureSession?.Dispose();
            _captureSession = null;
            _framePool?.Dispose();
            _framePool = null;
            _device?.Dispose();
            _device = null;
            _frames.Complete();
        }
    }

    private static void ValidateSize(SizeInt32 size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException(
                $"The selected window has invalid dimensions {size.Width} × {size.Height}.");
        }
    }

    private sealed class PendingFrame(SourceFrame frame) : IDisposable
    {
        private SourceFrame? _frame = frame;

        public SourceFrame TakeFrame()
        {
            SourceFrame frame = _frame
                ?? throw new InvalidOperationException("The pending frame has already been consumed.");
            _frame = null;
            return frame;
        }

        public void Dispose()
        {
            if (_frame?.Payload is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _frame = null;
        }
    }
}
