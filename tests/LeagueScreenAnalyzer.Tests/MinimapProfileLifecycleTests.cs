using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using System.Windows.Threading;
using LeagueScreenAnalyzer.App.Services;
using LeagueScreenAnalyzer.App.ViewModels;
using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeagueScreenAnalyzer.Tests;

public sealed class MinimapProfileLifecycleTests
{
    [Fact]
    public void ProfileSelection_DisablesDuringCaptureAndReenablesAfterStop() =>
        RunOnStaAsync(async () =>
        {
            FakeSession session = new();
            CaptureController controller = new(
                new FakeSelector(CaptureSelectionResult.Selected(session)));
            MainWindowViewModel viewModel = new(
                controller,
                new FakeHandleProvider(),
                Dispatcher.CurrentDispatcher,
                NullLogger<MainWindowViewModel>.Instance,
                layoutStore: new MemoryLayoutStore());
            try
            {
                Assert.True(viewModel.CanConfigureMinimap);
                Assert.Equal(
                    "league-replay-minimap-v1",
                    viewModel.ActiveMinimapProfileId);

                await controller.SelectWindowAsync(0);
                Assert.False(viewModel.CanConfigureMinimap);
                string selected = viewModel.MinimapProfileId;
                viewModel.MinimapProfileId = "missing-while-capturing";
                Assert.Equal(selected, viewModel.MinimapProfileId);

                await controller.StopAsync();
                Assert.True(viewModel.CanConfigureMinimap);
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });

    private static void RunOnStaAsync(Func<Task> action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(dispatcher));
                Task task = action();
                DispatcherFrame frame = new();
                _ = task.ContinueWith(
                    _ => dispatcher.BeginInvoke(() => frame.Continue = false),
                    TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class FakeSelector(CaptureSelectionResult selection)
        : ICaptureSessionSelector
    {
        public Task<CaptureSelectionResult> SelectWindowAsync(
            nint ownerWindowHandle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(selection);
    }

    private sealed class FakeSession : ILiveCaptureSession
    {
        private readonly Channel<SourceFrame> _frames =
            Channel.CreateUnbounded<SourceFrame>();

        public string SourceName => "Lifecycle test";
        public CaptureSessionEndReason EndReason { get; private set; }
        public string? EndErrorMessage => null;
        public Task StartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<SourceFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (SourceFrame frame in
                           _frames.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            EndReason = CaptureSessionEndReason.Stopped;
            _frames.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _frames.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHandleProvider : IWindowHandleProvider
    {
        public nint GetHandle() => 0;
    }

    private sealed class MemoryLayoutStore : ICaptureLayoutStore
    {
        public Task<IReadOnlyList<string>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task SaveAsync(
            CaptureLayout layout,
            bool overwrite,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CaptureLayout> LoadAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CaptureLayout>(new KeyNotFoundException(name));

        public Task DeleteAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
