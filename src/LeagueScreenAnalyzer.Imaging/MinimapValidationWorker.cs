using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public sealed record MinimapValidationObservation(
    MapImage Image,
    MapValidationResult Result);

public sealed class MinimapValidationFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } =
        exception ?? throw new ArgumentNullException(nameof(exception));
}

public sealed class MinimapValidationWorker(IMapImageValidator validator) : IAsyncDisposable
{
    private IMapImageValidator _validator =
        validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly object _gate = new();
    private readonly SemaphoreSlim _available = new(0, 1);
    private MapImage? _latest;
    private CancellationTokenSource? _cancellation;
    private Task? _pump;
    private bool _accepting;
    private bool _disposed;

    public event EventHandler<MinimapValidationObservation>? ObservationAvailable;

    public event EventHandler<MinimapValidationFailedEventArgs>? ValidationFailed;

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

    public string? ActiveProfileId =>
        (_validator as StructuralMinimapValidator)?.Profile.Id;

    public void SetValidator(IMapImageValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_accepting)
            {
                throw new InvalidOperationException(
                    "The minimap profile cannot change while validation is running.");
            }

            _validator = validator;
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

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _accepting = true;
            _pump = PumpAsync(_cancellation.Token);
        }
    }

    public bool TrySubmit(MapImage image)
    {
        image.Validate();
        MapImage? replaced;
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
                _available.Release();
            }
        }

        replaced?.Dispose();
        return true;
    }

    public async Task StopAsync()
    {
        MapImage? pending;
        CancellationTokenSource? cancellation;
        Task? pump;
        lock (_gate)
        {
            _accepting = false;
            pending = _latest;
            _latest = null;
            cancellation = _cancellation;
            pump = _pump;
            cancellation?.Cancel();
        }

        pending?.Dispose();
        if (pump is not null)
        {
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_gate)
        {
            if (ReferenceEquals(_pump, pump))
            {
                _pump = null;
                _cancellation = null;
            }

            while (_available.Wait(0))
            {
            }
        }

        cancellation?.Dispose();
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
        while (true)
        {
            MapImage? image = null;
            try
            {
                await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    image = _latest;
                    _latest = null;
                }

                if (image is null)
                {
                    continue;
                }

                MapValidationResult result =
                    await _validator.ValidateAsync(image, cancellationToken).ConfigureAwait(false);
                ObservationAvailable?.Invoke(this, new MinimapValidationObservation(image, result));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ValidationFailed?.Invoke(this, new MinimapValidationFailedEventArgs(exception));
            }
            finally
            {
                image?.Dispose();
            }
        }
    }
}
