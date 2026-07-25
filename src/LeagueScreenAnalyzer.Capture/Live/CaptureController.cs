using LeagueScreenAnalyzer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeagueScreenAnalyzer.Capture.Live;

public sealed class CaptureController : IAsyncDisposable
{
    private readonly ICaptureSessionSelector _selector;
    private readonly ILogger<CaptureController> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _captureCancellation;
    private ILiveCaptureSession? _session;
    private Task? _framePump;
    private bool _disposed;

    public CaptureController(
        ICaptureSessionSelector selector,
        ILogger<CaptureController>? logger = null)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _logger = logger ?? NullLogger<CaptureController>.Instance;
    }

    public CaptureState State { get; private set; } = new(CaptureStatus.Idle);

    public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;

    public event EventHandler<CaptureFrameEventArgs>? FrameArrived;

    public async Task SelectWindowAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (State.Status is CaptureStatus.Selecting or CaptureStatus.Capturing)
            {
                return;
            }

            await CleanupPreviousSessionAsync().ConfigureAwait(false);
            SetState(new CaptureState(CaptureStatus.Selecting));
            _logger.LogInformation("Opening Windows graphics-capture picker.");

            CaptureSelectionResult selection;
            try
            {
                selection = await _selector.SelectWindowAsync(ownerWindowHandle, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetState(new CaptureState(CaptureStatus.Stopped, ErrorMessage: "Window selection was cancelled."));
                throw;
            }
            catch (Exception exception)
            {
                Fail("Capture picker failed to open.", exception);
                return;
            }

            if (selection.Status == CaptureSelectionStatus.Cancelled)
            {
                _logger.LogInformation("Windows graphics-capture picker was cancelled.");
                SetState(new CaptureState(
                    CaptureStatus.Error,
                    ErrorMessage: "Window selection was cancelled. Select Window to try again."));
                return;
            }

            if (selection.Status == CaptureSelectionStatus.PermissionUnavailable)
            {
                string error = selection.ErrorMessage
                    ?? "Window capture permission is unavailable on this device.";
                Fail(error);
                return;
            }

            ILiveCaptureSession session = selection.Session
                ?? throw new InvalidOperationException("A selected capture result did not include a session.");
            _captureCancellation = new CancellationTokenSource();

            try
            {
                await session.StartAsync(_captureCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                _captureCancellation.Dispose();
                _captureCancellation = null;
                Fail("Capture initialization failed.", exception);
                return;
            }

            _session = session;
            SetState(new CaptureState(CaptureStatus.Capturing, session.SourceName));
            _logger.LogInformation("Capture started for {SourceName}.", session.SourceName);
            _framePump = PumpFramesAsync(session, _captureCancellation.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopCoreAsync(setStoppedState: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopCoreAsync(setStoppedState: false, CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private async Task PumpFramesAsync(ILiveCaptureSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (SourceFrame frame in session.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    SetState(State with
                    {
                        Width = frame.Width,
                        Height = frame.Height,
                        LatestSequence = frame.SequenceNumber,
                        LatestTimestamp = frame.SourceTimestamp
                    });
                    FrameArrived?.Invoke(this, new CaptureFrameEventArgs(frame));
                }
                finally
                {
                    (frame.Payload as IDisposable)?.Dispose();
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                HandleSessionEnd(session);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fail("Capture failed while receiving frames.", exception, session.SourceName);
        }
    }

    private void HandleSessionEnd(ILiveCaptureSession session)
    {
        string message = session.EndReason switch
        {
            CaptureSessionEndReason.SourceClosed =>
                "The selected window closed. Capture has stopped.",
            CaptureSessionEndReason.InvalidFrameSize =>
                "The selected window reported an invalid frame size. Capture has stopped.",
            CaptureSessionEndReason.Failure =>
                session.EndErrorMessage ?? "Capture stopped because of a platform failure.",
            _ => "Capture stopped."
        };

        if (session.EndReason == CaptureSessionEndReason.SourceClosed)
        {
            _logger.LogWarning("Selected window {SourceName} closed.", session.SourceName);
        }

        if (session.EndReason is CaptureSessionEndReason.SourceClosed
            or CaptureSessionEndReason.InvalidFrameSize
            or CaptureSessionEndReason.Failure)
        {
            SetState(State with { Status = CaptureStatus.Error, ErrorMessage = message });
        }
        else
        {
            SetState(State with { Status = CaptureStatus.Stopped, ErrorMessage = null });
        }
    }

    private async Task StopCoreAsync(bool setStoppedState, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ILiveCaptureSession? session = _session;
            Task? framePump = _framePump;
            CancellationTokenSource? captureCancellation = _captureCancellation;

            if (session is null)
            {
                if (setStoppedState && State.Status != CaptureStatus.Selecting)
                {
                    SetState(State with { Status = CaptureStatus.Stopped });
                }

                return;
            }

            captureCancellation?.Cancel();
            await session.StopAsync(cancellationToken).ConfigureAwait(false);

            if (framePump is not null)
            {
                try
                {
                    await framePump.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            await session.DisposeAsync().ConfigureAwait(false);
            captureCancellation?.Dispose();
            _session = null;
            _framePump = null;
            _captureCancellation = null;

            if (setStoppedState)
            {
                SetState(State with { Status = CaptureStatus.Stopped, ErrorMessage = null });
            }

            _logger.LogInformation("Capture stopped for {SourceName}.", session.SourceName);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task CleanupPreviousSessionAsync()
    {
        if (_session is null)
        {
            return;
        }

        _captureCancellation?.Cancel();
        try
        {
            await _session.StopAsync().ConfigureAwait(false);
            if (_framePump is not null)
            {
                await _framePump.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _captureCancellation?.Dispose();
            _session = null;
            _framePump = null;
            _captureCancellation = null;
        }
    }

    private void Fail(string message, Exception? exception = null, string? sourceName = null)
    {
        if (exception is null)
        {
            _logger.LogError("Capture failure: {Message}", message);
        }
        else
        {
            _logger.LogError(exception, "Capture failure: {Message}", message);
        }

        SetState(new CaptureState(
            CaptureStatus.Error,
            sourceName ?? State.SourceName,
            ErrorMessage: exception is null ? message : $"{message} {exception.Message}"));
    }

    private void SetState(CaptureState state)
    {
        State = state;
        StateChanged?.Invoke(this, new CaptureStateChangedEventArgs(state));
    }
}
