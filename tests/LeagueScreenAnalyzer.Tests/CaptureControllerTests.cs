using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class CaptureControllerTests
{
    [Fact]
    public async Task SelectWindowAsync_TransitionsToCapturing()
    {
        FakeSession session = new("Browser");
        await using CaptureController controller = new(new FakeSelector(
            CaptureSelectionResult.Selected(session)));
        List<CaptureStatus> statuses = [];
        controller.StateChanged += (_, args) => statuses.Add(args.State.Status);

        await controller.SelectWindowAsync(123);

        Assert.Equal(CaptureStatus.Capturing, controller.State.Status);
        Assert.Equal("Browser", controller.State.SourceName);
        Assert.Equal([CaptureStatus.Selecting, CaptureStatus.Capturing], statuses);
    }

    [Fact]
    public async Task SelectWindowAsync_CancelledSelectionShowsClearError()
    {
        await using CaptureController controller = new(new FakeSelector(
            CaptureSelectionResult.Cancelled()));

        await controller.SelectWindowAsync(123);

        Assert.Equal(CaptureStatus.Error, controller.State.Status);
        Assert.Contains("cancelled", controller.State.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(controller.State.CanSelect);
    }

    [Fact]
    public async Task SelectWindowAsync_InitializationFailureShowsErrorAndDisposesSession()
    {
        FakeSession session = new("Broken") { StartException = new InvalidOperationException("device lost") };
        await using CaptureController controller = new(new FakeSelector(
            CaptureSelectionResult.Selected(session)));

        await controller.SelectWindowAsync(123);

        Assert.Equal(CaptureStatus.Error, controller.State.Status);
        Assert.Contains("initialization", controller.State.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task SourceClosure_TransitionsToError()
    {
        FakeSession session = new("Player");
        await using CaptureController controller = new(new FakeSelector(
            CaptureSelectionResult.Selected(session)));
        TaskCompletionSource<CaptureState> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StateChanged += (_, args) =>
        {
            if (args.State.ErrorMessage?.Contains("closed", StringComparison.OrdinalIgnoreCase) == true)
            {
                closed.TrySetResult(args.State);
            }
        };
        await controller.SelectWindowAsync(123);

        session.Complete(CaptureSessionEndReason.SourceClosed);
        CaptureState state = await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(CaptureStatus.Error, state.Status);
        Assert.False(state.IsCapturing);
    }

    [Fact]
    public async Task StopAsync_StopsActiveCaptureAndRepeatedStopIsSafe()
    {
        FakeSession session = new("Browser");
        await using CaptureController controller = new(new FakeSelector(
            CaptureSelectionResult.Selected(session)));
        await controller.SelectWindowAsync(123);

        await controller.StopAsync();
        await controller.StopAsync();

        Assert.Equal(CaptureStatus.Stopped, controller.State.Status);
        Assert.Equal(1, session.StopCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task SelectWindowAsync_CanSelectAnotherSourceAfterStop()
    {
        FakeSession first = new("First");
        FakeSession second = new("Second");
        FakeSelector selector = new(
            CaptureSelectionResult.Selected(first),
            CaptureSelectionResult.Selected(second));
        await using CaptureController controller = new(selector);
        await controller.SelectWindowAsync(123);
        await controller.StopAsync();

        await controller.SelectWindowAsync(123);

        Assert.Equal(CaptureStatus.Capturing, controller.State.Status);
        Assert.Equal("Second", controller.State.SourceName);
        Assert.Equal(2, selector.SelectionCount);
    }

    [Fact]
    public async Task FrameDelivery_UpdatesDimensionsSequenceAndTimestamp()
    {
        FakeSession session = new("Browser");
        await using CaptureController controller = new(new FakeSelector(
            CaptureSelectionResult.Selected(session)));
        TaskCompletionSource<SourceFrame> arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.FrameArrived += (_, args) => arrived.TrySetResult(args.Frame);
        await controller.SelectWindowAsync(123);

        session.WriteFrame(42, TimeSpan.FromMilliseconds(250), 1280, 720);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1280, controller.State.Width);
        Assert.Equal(720, controller.State.Height);
        Assert.Equal(42, controller.State.LatestSequence);
        Assert.Equal(TimeSpan.FromMilliseconds(250), controller.State.LatestTimestamp);
    }

    private sealed class FakeSelector(params CaptureSelectionResult[] selections) : ICaptureSessionSelector
    {
        private readonly Queue<CaptureSelectionResult> _selections = new(selections);

        public int SelectionCount { get; private set; }

        public Task<CaptureSelectionResult> SelectWindowAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default)
        {
            SelectionCount++;
            return Task.FromResult(_selections.Dequeue());
        }
    }

    private sealed class FakeSession(string sourceName) : ILiveCaptureSession
    {
        private readonly Channel<SourceFrame> _frames = Channel.CreateUnbounded<SourceFrame>();

        public string SourceName { get; } = sourceName;

        public CaptureSessionEndReason EndReason { get; private set; }

        public string? EndErrorMessage { get; private set; }

        public Exception? StartException { get; init; }

        public int StopCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            StartException is null ? Task.CompletedTask : Task.FromException(StartException);

        public async IAsyncEnumerable<SourceFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (SourceFrame frame in _frames.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (EndReason == CaptureSessionEndReason.None)
            {
                StopCount++;
                Complete(CaptureSessionEndReason.Stopped);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _frames.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public void WriteFrame(long sequence, TimeSpan timestamp, int width, int height)
        {
            _frames.Writer.TryWrite(new SourceFrame(
                sequence,
                timestamp,
                width,
                height,
                new FakePayload()));
        }

        public void Complete(CaptureSessionEndReason reason, string? error = null)
        {
            EndReason = reason;
            EndErrorMessage = error;
            _frames.Writer.TryComplete();
        }
    }

    private sealed class FakePayload : IFramePayload;
}
