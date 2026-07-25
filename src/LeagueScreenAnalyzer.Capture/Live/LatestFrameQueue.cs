using System.Runtime.CompilerServices;

namespace LeagueScreenAnalyzer.Capture.Live;

public sealed class LatestFrameQueue<T> : IAsyncDisposable
    where T : class, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _available = new(0, 1);
    private T? _latest;
    private bool _completed;

    public bool TryWrite(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        T? replaced;

        lock (_gate)
        {
            if (_completed)
            {
                item.Dispose();
                return false;
            }

            replaced = _latest;
            _latest = item;
            if (replaced is null)
            {
                _available.Release();
            }
        }

        replaced?.Dispose();
        return true;
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            if (_latest is null)
            {
                _available.Release();
            }
        }
    }

    public async IAsyncEnumerable<T> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            T? item;
            bool completed;

            lock (_gate)
            {
                item = _latest;
                _latest = null;
                completed = _completed;
            }

            if (item is not null)
            {
                yield return item;
                if (completed)
                {
                    yield break;
                }

                continue;
            }

            if (completed)
            {
                yield break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        T? remaining;
        lock (_gate)
        {
            _completed = true;
            remaining = _latest;
            _latest = null;
        }

        remaining?.Dispose();
        _available.Dispose();
        return ValueTask.CompletedTask;
    }
}
