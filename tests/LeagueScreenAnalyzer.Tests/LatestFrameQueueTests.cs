using LeagueScreenAnalyzer.Capture.Live;

namespace LeagueScreenAnalyzer.Tests;

public sealed class LatestFrameQueueTests
{
    [Fact]
    public async Task TryWrite_ReplacesAndDisposesStaleFrame()
    {
        await using LatestFrameQueue<DisposableFrame> queue = new();
        DisposableFrame stale = new(1);
        DisposableFrame latest = new(2);

        Assert.True(queue.TryWrite(stale));
        Assert.True(queue.TryWrite(latest));
        queue.Complete();

        DisposableFrame delivered = Assert.Single(await CollectAsync(queue.ReadAllAsync()));
        Assert.True(stale.Disposed);
        Assert.Same(latest, delivered);
    }

    [Fact]
    public async Task TryWrite_DeliveryRemainsBoundedToOnePendingFrame()
    {
        await using LatestFrameQueue<DisposableFrame> queue = new();
        DisposableFrame[] frames = Enumerable.Range(0, 100)
            .Select(index => new DisposableFrame(index))
            .ToArray();

        foreach (DisposableFrame frame in frames)
        {
            queue.TryWrite(frame);
        }

        queue.Complete();
        DisposableFrame delivered = Assert.Single(await CollectAsync(queue.ReadAllAsync()));

        Assert.Equal(99, delivered.Value);
        Assert.Equal(99, frames.Count(frame => frame.Disposed));
    }

    private sealed class DisposableFrame(int value) : IDisposable
    {
        public int Value { get; } = value;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        List<T> collected = [];
        await foreach (T value in values)
        {
            collected.Add(value);
        }

        return collected;
    }
}
