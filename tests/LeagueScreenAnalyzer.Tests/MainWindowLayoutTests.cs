using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LeagueScreenAnalyzer.App;
using LeagueScreenAnalyzer.App.ViewModels;

namespace LeagueScreenAnalyzer.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void ClockRecognition_IsVisibleAtDefaultSizeAndReachableWhenWindowIsSmaller() =>
        RunOnSta(() =>
        {
            MainWindow window = new()
            {
                Width = 1280,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
                ShowActivated = false
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                ScrollViewer scrollViewer =
                    RequiredElement<ScrollViewer>(window, "RightControlsScrollViewer");
                GroupBox recognitionGroup =
                    RequiredElement<GroupBox>(window, "ClockRecognitionGroup");

                Assert.Equal(Visibility.Visible, recognitionGroup.Visibility);
                Assert.True(recognitionGroup.IsVisible);
                Assert.True(recognitionGroup.ActualHeight > 0);
                Assert.Same(scrollViewer, FindAncestor<ScrollViewer>(recognitionGroup));
                Assert.Equal(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);
                Rect recognitionBounds = BoundsRelativeTo(recognitionGroup, scrollViewer);
                Assert.True(
                    IsFullyInsideViewport(recognitionGroup, scrollViewer),
                    $"Recognition bounds {recognitionBounds}; viewport height {scrollViewer.ActualHeight}.");

                AssertRequiredRecognitionControls(window);

                CheckBox enabledToggle =
                    RequiredElement<CheckBox>(window, "RecognitionEnabledToggle");
                MainWindowViewModel viewModel =
                    Assert.IsType<MainWindowViewModel>(window.DataContext);
                Assert.Equal(
                    ["league-replay-v1", "league-replay-v2", "league-replay-v3"],
                    viewModel.AvailableClockProfiles.Select(profile => profile.Id).ToArray());
                viewModel.SelectedClockProfileId = "league-replay-v3";
                Assert.Equal("league-replay-v3", viewModel.SelectedClockProfileId);
                Assert.Equal("league-replay-v3", viewModel.ActiveClockProfileId);
                Assert.Equal(135, viewModel.SelectedClockProfileTemplateCount);
                Assert.Contains(
                    "league-replay-v3",
                    RequiredElement<TextBlock>(window, "SelectedClockProfileIdText").Text);
                Assert.False(viewModel.RestorePersistedClockProfile(
                    "missing-profile",
                    "missing-layout"));
                Assert.Equal("league-replay-v3", viewModel.SelectedClockProfileId);
                Assert.Contains("missing-profile", viewModel.ClockProfileWarning);
                enabledToggle.IsChecked = false;
                Assert.False(viewModel.RecognitionEnabled);
                enabledToggle.IsChecked = true;
                Assert.True(viewModel.RecognitionEnabled);

                window.Width = 960;
                window.Height = 700;
                window.UpdateLayout();

                Assert.True(recognitionGroup.IsVisible);
                Assert.True(scrollViewer.ScrollableHeight > 0);

                Button saveButton =
                    RequiredElement<Button>(window, "SaveClockSampleButton");
                saveButton.BringIntoView();
                window.UpdateLayout();
                Assert.True(IsInsideViewport(saveButton, scrollViewer));
            }
            finally
            {
                window.Close();
            }
        });

    private static void AssertRequiredRecognitionControls(FrameworkElement window)
    {
        Assert.NotNull(RequiredElement<ComboBox>(window, "ClockProfileSelector").SelectedItem);
        Assert.NotNull(RequiredElement<ComboBox>(window, "PlaybackSpeedSelector").SelectedItem);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockStatusText").Text);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockRecognizedCandidateText").Text);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockAcceptedGameTimeText").Text);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockHistoricalGameTimeText").Text);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockConfidenceText").Text);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockDiagnosticText").Text);
        Assert.NotNull(RequiredElement<TextBlock>(window, "SelectedClockProfileIdText").Text);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockProfileWarningText"));
        Assert.Equal(
            "Actual clock value",
            RequiredElement<TextBox>(window, "ActualClockValueTextBox")
                .GetValue(System.Windows.Automation.AutomationProperties.NameProperty));
        Assert.Equal(
            "Save as unlabeled diagnostic only",
            RequiredElement<CheckBox>(window, "SaveUnlabeledClockSampleCheckBox").Content);
        Assert.NotNull(RequiredElement<TextBlock>(window, "ClockLabelValidationText"));
        Assert.Equal(
            "Save Clock Sample",
            RequiredElement<Button>(window, "SaveClockSampleButton").Content);
    }

    private static bool IsFullyInsideViewport(
        FrameworkElement element,
        FrameworkElement viewport)
    {
        Rect bounds = BoundsRelativeTo(element, viewport);
        return bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight;
    }

    private static bool IsInsideViewport(
        FrameworkElement element,
        FrameworkElement viewport)
    {
        Rect bounds = BoundsRelativeTo(element, viewport);
        return bounds.Bottom > 0 && bounds.Top < viewport.ActualHeight;
    }

    private static Rect BoundsRelativeTo(
        FrameworkElement element,
        FrameworkElement ancestor)
    {
        GeneralTransform transform = element.TransformToAncestor(ancestor);
        return transform.TransformBounds(new Rect(element.RenderSize));
    }

    private static T RequiredElement<T>(FrameworkElement root, string name)
        where T : FrameworkElement =>
        Assert.IsType<T>(root.FindName(name));

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
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
}
