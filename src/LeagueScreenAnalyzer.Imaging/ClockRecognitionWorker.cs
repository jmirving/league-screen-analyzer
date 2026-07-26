using System.Diagnostics;
using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public sealed record ClockRecognitionObservation(
    ClockImage Image,
    ClockRecognitionResult Recognition,
    ClockReading Reading,
    double ActualSamplesPerSecond);

public sealed class ClockRecognitionFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } =
        exception ?? throw new ArgumentNullException(nameof(exception));
}

public sealed class ClockRecognitionWorker : IAsyncDisposable
{
    private readonly IClockImageRecognizer _recognizer;
    private readonly IClockTemporalValidator _validator;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _available = new(0, 1);
    private ClockRecognitionProfile _profile;
    private ClockImage? _latest;
    private CancellationTokenSource? _cancellation;
    private Task? _pump;
    private long _processed;
    private Stopwatch? _cadenceClock;
    private Task? _stopTask;
    private bool _accepting;
    private bool _disposed;

    public ClockRecognitionWorker(
        IClockImageRecognizer recognizer,
        IClockTemporalValidator validator,
        ClockRecognitionProfile profile)
    {
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _profile = (profile ?? throw new ArgumentNullException(nameof(profile))).Validate();
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _accepting && _pump is { IsCompleted: false };
            }
        }
    }

    public ClockRecognitionProfile Profile => _profile;

    public event EventHandler<ClockRecognitionObservation>? ObservationAvailable;

    public event EventHandler<ClockRecognitionFailedEventArgs>? RecognitionFailed;

    public void SetProfile(ClockRecognitionProfile profile)
    {
        lock (_gate)
        {
            if (_pump is { IsCompleted: false })
            {
                throw new InvalidOperationException("Clock profile and playback speed are immutable while recognition is active.");
            }

            _profile = (profile ?? throw new ArgumentNullException(nameof(profile))).Validate();
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pump is { IsCompleted: false })
            {
                return;
            }

            CleanupCompletedRun();
            _validator.Reset();
            _processed = 0;
            _cadenceClock = Stopwatch.StartNew();
            _cancellation = new CancellationTokenSource();
            _accepting = true;
            _pump = PumpAsync(_cancellation.Token);
        }
    }

    public bool TrySubmit(ClockImage image)
    {
        image.Validate();
        ClockImage? replaced;
        lock (_gate)
        {
            if (_disposed || !_accepting || _pump is not { IsCompleted: false })
            {
                image.Dispose();
                return false;
            }

            replaced = _latest;
            _latest = image;
            if (replaced is null)
            {
                // The availability decision and signal are one atomic operation with
                // respect to producers and the consumer taking the pending slot.
                _available.Release();
            }
        }

        replaced?.Dispose();
        return true;
    }

    public Task StopAsync()
    {
        ClockImage? pending;
        Task stopTask;
        lock (_gate)
        {
            if (_stopTask is { IsCompleted: false })
            {
                return _stopTask;
            }

            if (_cancellation is null || _pump is null)
            {
                return Task.CompletedTask;
            }

            _accepting = false;
            pending = _latest;
            _latest = null;
            _cancellation.Cancel();
            stopTask = FinishStopAsync(_cancellation, _pump);
            _stopTask = stopTask;
        }

        pending?.Dispose();
        return stopTask;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _accepting = false;
        }

        await StopAsync().ConfigureAwait(false);
        _available.Dispose();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        double targetRate = Math.Min(
            _profile.MaximumSamplesPerSecond,
            Math.Max(1, 4 * _profile.PlaybackSpeed));
        TimeSpan minimumInterval = TimeSpan.FromSeconds(1 / targetRate);
        Stopwatch processingClock = Stopwatch.StartNew();
        TimeSpan lastStart = -minimumInterval;

        while (true)
        {
            ClockImage? image = null;
            try
            {
                await _available.WaitAsync(cancellationToken).ConfigureAwait(false);

                TimeSpan wait = minimumInterval - (processingClock.Elapsed - lastStart);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }

                lock (_gate)
                {
                    image = _latest;
                    _latest = null;
                }

                if (image is null)
                {
                    continue;
                }

                lastStart = processingClock.Elapsed;
                ClockRecognitionResult recognition =
                    await _recognizer.RecognizeAsync(image, _profile, cancellationToken).ConfigureAwait(false);
                ClockReading reading = _validator.Validate(
                    recognition,
                    _profile,
                    image.SourceFrameSequence,
                    image.SourceTimestamp);
                _processed++;
                double seconds = Math.Max(0.001, _cadenceClock?.Elapsed.TotalSeconds ?? 0.001);
                ObservationAvailable?.Invoke(
                    this,
                    new ClockRecognitionObservation(image, recognition, reading, _processed / seconds));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ClockImage? pending;
                lock (_gate)
                {
                    _accepting = false;
                    pending = _latest;
                    _latest = null;
                }

                pending?.Dispose();
                RecognitionFailed?.Invoke(this, new ClockRecognitionFailedEventArgs(exception));
                return;
            }
            finally
            {
                image?.Dispose();
            }
        }
    }

    private async Task FinishStopAsync(CancellationTokenSource cancellation, Task pump)
    {
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_gate)
            {
                while (_available.Wait(0))
                {
                }

                cancellation.Dispose();
                if (ReferenceEquals(_cancellation, cancellation))
                {
                    _cancellation = null;
                    _pump = null;
                    _accepting = false;
                }
            }
        }
    }

    private void CleanupCompletedRun()
    {
        if (_pump is not { IsCompleted: true })
        {
            return;
        }

        _cancellation?.Dispose();
        _cancellation = null;
        _pump = null;
        _stopTask = null;
        _accepting = false;
        while (_available.Wait(0))
        {
        }
    }
}
