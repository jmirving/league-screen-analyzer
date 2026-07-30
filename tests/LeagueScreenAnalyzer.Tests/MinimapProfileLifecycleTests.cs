using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using System.Windows.Threading;
using LeagueScreenAnalyzer.App.Services;
using LeagueScreenAnalyzer.App.ViewModels;
using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;
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

    [Fact]
    public void PersistedOlderClockSelection_IsPreservedAcrossCaptureAndCatalogRefresh() =>
        RunOnStaAsync(async () =>
        {
            FakeSession session = new();
            CaptureController controller = new(
                new FakeSelector(CaptureSelectionResult.Selected(session)));
            ClockProfileCatalog clockCatalog = ClockProfileCatalog.CreateDefault();
            MinimapProfileCatalog minimapCatalog = MinimapProfileCatalog.CreateDefault();
            MainWindowViewModel viewModel = new(
                controller,
                new FakeHandleProvider(),
                Dispatcher.CurrentDispatcher,
                NullLogger<MainWindowViewModel>.Instance,
                layoutStore: new MemoryLayoutStore(),
                clockProfileCatalog: clockCatalog,
                minimapProfileCatalog: minimapCatalog);
            try
            {
                Assert.Equal(clockCatalog.DefaultProfile.Id, viewModel.SelectedClockProfileId);
                Assert.True(viewModel.RestorePersistedClockProfile(
                    BuiltInClockProfiles.LeagueReplayV2Id,
                    "older-layout"));
                Assert.Equal(
                    BuiltInClockProfiles.LeagueReplayV2Id,
                    viewModel.SelectedClockProfileId);

                await controller.SelectWindowAsync(0);
                Assert.Equal(
                    BuiltInClockProfiles.LeagueReplayV2Id,
                    viewModel.ActiveClockProfileId);
                viewModel.RefreshProfileCatalogs(
                    ClockProfileCatalog.CreateDefault(),
                    MinimapProfileCatalog.CreateDefault());
                Assert.Equal(
                    BuiltInClockProfiles.LeagueReplayV2Id,
                    viewModel.ActiveClockProfileId);

                await controller.StopAsync();
                Assert.Equal(
                    BuiltInClockProfiles.LeagueReplayV2Id,
                    viewModel.SelectedClockProfileId);
                Assert.Equal(
                    BuiltInClockProfiles.LeagueReplayV2Id,
                    viewModel.ActiveClockProfileId);
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });

    [Fact]
    public void MissingPersistedSelection_WarnsSuggestsAndDoesNotFallbackSilently() =>
        RunOnStaAsync(async () =>
        {
            CaptureController controller = new(
                new FakeSelector(CaptureSelectionResult.Cancelled()));
            MainWindowViewModel viewModel = new(
                controller,
                new FakeHandleProvider(),
                Dispatcher.CurrentDispatcher,
                NullLogger<MainWindowViewModel>.Instance,
                layoutStore: new MemoryLayoutStore());
            try
            {
                string original = viewModel.SelectedClockProfileId;

                Assert.False(viewModel.RestorePersistedClockProfile(
                    "league-replay-v999",
                    "missing-layout"));

                Assert.Equal(original, viewModel.SelectedClockProfileId);
                Assert.Contains("unavailable", viewModel.ClockProfileWarning);
                Assert.Contains("was not changed", viewModel.ClockProfileWarning);
                Assert.Contains(
                    $"Suggested compatible replacement: '{original}'",
                    viewModel.ClockProfileWarning);

                viewModel.SelectedClockProfileId = "league-replay-v998";
                Assert.Equal(original, viewModel.SelectedClockProfileId);
                Assert.Contains("Suggested compatible replacement", viewModel.ClockProfileWarning);
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
