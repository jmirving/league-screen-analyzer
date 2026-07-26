using LeagueScreenAnalyzer.Core.Abstractions;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockRecognitionWorkerTests
{
    [Fact]
    public async Task Worker_FirstSubmissionWakesWaitingConsumerAndDisposesConsumedSample()
    {
        RecordingRecognizer recognizer = new();
        await using ClockRecognitionWorker worker = CreateWorker(recognizer);
        DisposeProbe owner = new();

        worker.Start();
        Assert.True(worker.TrySubmit(Image(1, owner)));

        Assert.Equal(1, await recognizer.NextSequence.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await owner.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task Worker_DiscardsStalePendingSamples()
    {
        BlockingRecognizer recognizer = new();
        await using ClockRecognitionWorker worker = new(
            recognizer,
            new ClockTemporalValidator(),
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id));
        TaskCompletionSource<ClockRecognitionObservation> observed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.ObservationAvailable += (_, value) =>
        {
            if (value.Image.SourceFrameSequence == 3)
            {
                observed.TrySetResult(value);
            }
        };

        worker.Start();
        worker.TrySubmit(ClockTestImages.Render("0:00", sequence: 1));
        await recognizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        worker.TrySubmit(ClockTestImages.Render("0:01", sequence: 2, timestampSeconds: 1));
        worker.TrySubmit(ClockTestImages.Render("0:02", sequence: 3, timestampSeconds: 2));
        recognizer.Release.TrySetResult();

        ClockRecognitionObservation result = await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(3, result.Image.SourceFrameSequence);
        Assert.DoesNotContain(2, recognizer.Sequences);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Worker_ThousandsOfReplacementsStayBoundedAndDisposeEveryStaleSampleOnce()
    {
        BlockingRecognizer recognizer = new();
        await using ClockRecognitionWorker worker = CreateWorker(recognizer);
        const int replacementCount = 5_000;
        DisposeProbe[] owners = Enumerable.Range(0, replacementCount + 1)
            .Select(_ => new DisposeProbe())
            .ToArray();
        TaskCompletionSource<long> latestObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.ObservationAvailable += (_, observation) =>
        {
            if (observation.Image.SourceFrameSequence == replacementCount)
            {
                latestObserved.TrySetResult(observation.Image.SourceFrameSequence);
            }
        };

        worker.Start();
        Assert.True(worker.TrySubmit(Image(0, owners[0])));
        await recognizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        for (int sequence = 1; sequence <= replacementCount; sequence++)
        {
            Assert.True(worker.TrySubmit(Image(sequence, owners[sequence])));
        }

        Assert.All(owners.Skip(1).Take(replacementCount - 1), owner =>
            Assert.Equal(1, owner.DisposeCount));
        recognizer.Release.TrySetResult();

        Assert.Equal(
            replacementCount,
            await latestObserved.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        await owners[replacementCount].Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([0L, replacementCount], recognizer.Sequences);
        Assert.All(owners, owner => Assert.Equal(1, owner.DisposeCount));
    }

    [Fact]
    public async Task Worker_ConcurrentProducersAndConsumerTakeDoNotOverflowOrLeak()
    {
        RecordingRecognizer recognizer = new();
        await using ClockRecognitionWorker worker = CreateWorker(recognizer);
        const int producerCount = 250;
        DisposeProbe[] owners = Enumerable.Range(0, producerCount)
            .Select(_ => new DisposeProbe())
            .ToArray();
        using ManualResetEventSlim start = new();

        worker.Start();
        Task[] producers = Enumerable.Range(0, producerCount)
            .Select(sequence => Task.Run(() =>
            {
                start.Wait();
                worker.TrySubmit(Image(sequence, owners[sequence]));
            }))
            .ToArray();
        start.Set();
        await Task.WhenAll(producers);
        await worker.StopAsync();

        Assert.All(owners, owner => Assert.Equal(1, owner.DisposeCount));
    }

    [Fact]
    public async Task Worker_SubmitRacingStopAndSubmitAfterDisposeRejectAndDisposeInputs()
    {
        BlockingRecognizer recognizer = new();
        ClockRecognitionWorker worker = CreateWorker(recognizer);
        DisposeProbe activeOwner = new();
        DisposeProbe pendingOwner = new();
        DisposeProbe racingOwner = new();

        worker.Start();
        worker.TrySubmit(Image(1, activeOwner));
        await recognizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        worker.TrySubmit(Image(2, pendingOwner));

        Task stop = worker.StopAsync();
        bool acceptedWhileStopping = worker.TrySubmit(Image(3, racingOwner));
        await stop;
        await worker.StopAsync();
        await worker.DisposeAsync();
        DisposeProbe afterDisposeOwner = new();
        bool acceptedAfterDispose = worker.TrySubmit(Image(4, afterDisposeOwner));
        await worker.DisposeAsync();

        Assert.False(acceptedWhileStopping);
        Assert.False(acceptedAfterDispose);
        Assert.Equal(1, activeOwner.DisposeCount);
        Assert.Equal(1, pendingOwner.DisposeCount);
        Assert.Equal(1, racingOwner.DisposeCount);
        Assert.Equal(1, afterDisposeOwner.DisposeCount);
    }

    [Fact]
    public async Task Worker_StopWhileWaitingExitsAndCanStartSecondSession()
    {
        RecordingRecognizer recognizer = new();
        await using ClockRecognitionWorker worker = CreateWorker(recognizer);

        worker.Start();
        await worker.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync();

        worker.Start();
        Assert.True(worker.TrySubmit(Image(7)));
        Assert.Equal(7, await recognizer.NextSequence.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        await worker.StopAsync();
    }

    [Fact]
    public async Task Worker_SlowRecognizerWithHighRateStreamDoesNotOverflow()
    {
        BlockingRecognizer recognizer = new();
        await using ClockRecognitionWorker worker = CreateWorker(recognizer);
        worker.Start();
        worker.TrySubmit(Image(0));
        await recognizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Parallel.For(1, 10_001, sequence =>
        {
            Assert.True(worker.TrySubmit(Image(sequence)));
        });

        recognizer.Release.TrySetResult();
        await worker.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Worker_RecognizerFailureIsReportedAsRecognitionFailure()
    {
        InvalidOperationException failure = new("classifier failed");
        await using ClockRecognitionWorker worker = CreateWorker(new ThrowingRecognizer(failure));
        TaskCompletionSource<ClockRecognitionFailedEventArgs> reported =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.RecognitionFailed += (_, args) => reported.TrySetResult(args);

        worker.Start();
        worker.TrySubmit(Image(1));

        ClockRecognitionFailedEventArgs result =
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(failure, result.Exception);
        Assert.False(worker.IsRunning);
    }

    [Fact]
    public void Worker_DisallowsSpeedChangeWhileRunning()
    {
        using CancellationTokenSource cancellation = new();
        ClockRecognitionWorker worker = new(
            new ConstrainedClockImageRecognizer(),
            new ClockTemporalValidator(),
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id));
        worker.Start();
        Assert.Throws<InvalidOperationException>(() =>
            worker.SetProfile(worker.Profile.WithPlaybackSpeed(4)));
        worker.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task DiagnosticWriter_WritesOriginalNormalizedSegmentsAndJson()
    {
        ClockImage image = ClockTestImages.Render("0:00");
        ClockRecognitionProfile profile =
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id);
        ClockRecognitionResult recognition =
            await new ConstrainedClockImageRecognizer().RecognizeAsync(image, profile);
        ClockReading reading =
            new ClockTemporalValidator().Validate(recognition, profile, 0, TimeSpan.Zero);
        using TemporaryDirectory temporary = new();

        Assert.True(ClockSampleLabelParser.TryParse(
            "0:00",
            out ClockSampleLabel? label,
            out _));
        string path = new ClockDiagnosticWriter().Write(
            temporary.Path,
            new ClockRecognitionObservation(image, recognition, reading, 4),
            profile,
            label,
            isUnlabeledDiagnostic: false);

        Assert.True(File.Exists(Path.Combine(path, "original-clock.bmp")));
        Assert.True(File.Exists(Path.Combine(path, "normalized-clock.pgm")));
        Assert.True(File.Exists(Path.Combine(path, "segment-00.pgm")));
        Assert.True(File.Exists(Path.Combine(path, "result.json")));
        string json = File.ReadAllText(Path.Combine(path, "result.json"));
        Assert.Contains("\"sampleKind\": \"labeled\"", json);
        Assert.Contains("\"explicitLabel\": \"0:00\"", json);
        Assert.Contains("\"explicitLabelSeconds\": 0", json);
        Assert.Contains("\"explicitLabelMilliseconds\": 0", json);
    }

    [Fact]
    public async Task DiagnosticWriter_WritesExplicitUnlabeledSample()
    {
        ClockImage image = ClockTestImages.Render("3:40");
        ClockRecognitionProfile profile =
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id);
        ClockRecognitionResult recognition =
            await new ConstrainedClockImageRecognizer().RecognizeAsync(image, profile);
        ClockReading reading =
            new ClockTemporalValidator().Validate(recognition, profile, 0, TimeSpan.Zero);
        using TemporaryDirectory temporary = new();

        string path = new ClockDiagnosticWriter().Write(
            temporary.Path,
            new ClockRecognitionObservation(image, recognition, reading, 4),
            profile,
            explicitLabel: null,
            isUnlabeledDiagnostic: true);

        string json = File.ReadAllText(Path.Combine(path, "result.json"));
        Assert.Contains("\"sampleKind\": \"unlabeledDiagnostic\"", json);
        Assert.Contains("\"explicitLabel\": null", json);
        Assert.Contains("\"explicitLabelSeconds\": null", json);
    }

    [Fact]
    public async Task DiagnosticWriter_RequiresIntentionalLabelMode()
    {
        ClockImage image = ClockTestImages.Render("3:40");
        ClockRecognitionProfile profile =
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id);
        ClockRecognitionResult recognition =
            await new ConstrainedClockImageRecognizer().RecognizeAsync(image, profile);
        ClockReading reading =
            new ClockTemporalValidator().Validate(recognition, profile, 0, TimeSpan.Zero);
        using TemporaryDirectory temporary = new();

        Assert.Throws<ArgumentException>(() => new ClockDiagnosticWriter().Write(
            temporary.Path,
            new ClockRecognitionObservation(image, recognition, reading, 4),
            profile,
            explicitLabel: null,
            isUnlabeledDiagnostic: false));
    }

    private sealed class BlockingRecognizer : IClockImageRecognizer
    {
        private int _callCount;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<long> Sequences { get; } = [];

        public async ValueTask<ClockRecognitionResult> RecognizeAsync(
            ClockImage image,
            ClockRecognitionProfile profile,
            CancellationToken cancellationToken = default)
        {
            Sequences.Add(image.SourceFrameSequence);
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return await new ConstrainedClockImageRecognizer().RecognizeAsync(
                image,
                profile,
                cancellationToken);
        }
    }

    private sealed class RecordingRecognizer : IClockImageRecognizer
    {
        public TaskCompletionSource<long> NextSequence { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ClockRecognitionResult> RecognizeAsync(
            ClockImage image,
            ClockRecognitionProfile profile,
            CancellationToken cancellationToken = default)
        {
            NextSequence.TrySetResult(image.SourceFrameSequence);
            return await new ConstrainedClockImageRecognizer().RecognizeAsync(
                image,
                profile,
                cancellationToken);
        }
    }

    private sealed class ThrowingRecognizer(Exception exception) : IClockImageRecognizer
    {
        public ValueTask<ClockRecognitionResult> RecognizeAsync(
            ClockImage image,
            ClockRecognitionProfile profile,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ClockRecognitionResult>(exception);
    }

    private sealed class DisposeProbe : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            Disposed.TrySetResult();
        }
    }

    private static ClockRecognitionWorker CreateWorker(IClockImageRecognizer recognizer) =>
        new(
            recognizer,
            new ClockTemporalValidator(),
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id)
                .WithPlaybackSpeed(8));

    private static ClockImage Image(long sequence, IDisposable? owner = null) =>
        ClockTestImages.Render(
            "0:00",
            sequence: sequence,
            timestampSeconds: sequence) with
        {
            Owner = owner
        };
}
