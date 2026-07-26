using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class MinimapValidationWorkerTests
{
    [Fact]
    public async Task RapidReplacementAndRepeatedStop_DisposeEveryOwnedImageExactlyOnce()
    {
        SlowValidator validator = new();
        await using MinimapValidationWorker worker = new(validator);
        List<CountingOwner> owners = [];
        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.ObservationAvailable += (_, _) => observed.TrySetResult();
        worker.Start();

        for (int sequence = 0; sequence < 25; sequence++)
        {
            CountingOwner owner = new();
            owners.Add(owner);
            worker.TrySubmit(Image(sequence, owner));
        }

        validator.Release();
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync();
        await worker.StopAsync();

        Assert.All(owners, owner => Assert.Equal(1, owner.DisposeCount));
    }

    private static MapImage Image(long sequence, IDisposable owner)
    {
        byte[] pixels = new byte[64 * 64 * 4];
        return new MapImage(
            64,
            64,
            64 * 4,
            pixels,
            sequence,
            TimeSpan.FromMilliseconds(sequence),
            owner);
    }

    private sealed class SlowValidator : IMapImageValidator
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async ValueTask<MapValidationResult> ValidateAsync(
            MapImage minimapImage,
            CancellationToken cancellationToken = default)
        {
            await _release.Task.WaitAsync(cancellationToken);
            return new MapValidationResult(
                MapFrameStatus.LowConfidence,
                0,
                ["test"],
                "test",
                sourceFrameSequence: minimapImage.SourceFrameSequence,
                sourceTimestamp: minimapImage.SourceTimestamp);
        }
    }

    private sealed class CountingOwner : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposeCount) != 1)
            {
                throw new InvalidOperationException("Owner disposed more than once.");
            }
        }
    }
}
