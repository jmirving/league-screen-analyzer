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
                recognitionGroup.BringIntoView();
                window.UpdateLayout();
                Assert.True(IsInsideViewport(recognitionGroup, scrollViewer));

                AssertRequiredRecognitionControls(window);
                MainWindowViewModel viewModel =
                    Assert.IsType<MainWindowViewModel>(window.DataContext);
                ComboBox minimapSelector =
                    RequiredElement<ComboBox>(window, "MinimapProfileSelector");
                Assert.True(minimapSelector.IsEnabled);
                Assert.Equal(
                    ["league-replay-minimap-v1"],
                    viewModel.AvailableMinimapProfiles.Select(profile => profile.Id).ToArray());
                Assert.Equal(
                    "league-replay-minimap-v1",
                    viewModel.MinimapProfileId);
                Assert.Equal(
                    "league-replay-minimap-v1",
                    viewModel.ActiveMinimapProfileId);
                Assert.NotNull(minimapSelector.SelectedItem);
                Assert.Contains(
                    "league-replay-minimap-v1",
                    RequiredElement<TextBlock>(
                        window,
                        "SelectedMinimapProfileIdText").Text);
                viewModel.MinimapProfileId = "missing-minimap-profile";
                Assert.Equal(
                    "league-replay-minimap-v1",
                    viewModel.MinimapProfileId);
                Assert.Contains(
                    "missing-minimap-profile",
                    viewModel.MinimapProfileWarning);

                CheckBox enabledToggle =
                    RequiredElement<CheckBox>(window, "RecognitionEnabledToggle");
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

    [Fact]
    public void RightControlSections_HaveDistinctAutoRowsAndNonOverlappingRenderedBounds() =>
        RunOnSta(() =>
        {
            MainWindow window = CreateWindow(1280, 900);

            try
            {
                window.Show();
                window.UpdateLayout();

                ScrollViewer scrollViewer =
                    RequiredElement<ScrollViewer>(window, "RightControlsScrollViewer");
                GroupBox[] sections =
                [
                    RequiredElement<GroupBox>(window, "ClockRecognitionGroup"),
                    RequiredElement<GroupBox>(window, "RegionsGroup"),
                    RequiredElement<GroupBox>(window, "LayoutsGroup"),
                    RequiredElement<GroupBox>(window, "MinimapValidationGroup")
                ];

                Assert.All(sections, section =>
                {
                    Assert.True(section.IsVisible);
                    Assert.True(section.ActualHeight > 0);
                    Assert.Same(scrollViewer, FindAncestor<ScrollViewer>(section));
                });

                Grid sectionGrid = Assert.IsType<Grid>(
                    VisualTreeHelper.GetParent(sections[0]));
                Assert.Equal(4, sectionGrid.RowDefinitions.Count);
                Assert.All(
                    sectionGrid.RowDefinitions,
                    row => Assert.True(row.Height.IsAuto));
                Assert.Equal([0, 1, 2, 3], sections.Select(Grid.GetRow).ToArray());
                Assert.Equal(4, sections.Select(Grid.GetRow).Distinct().Count());

                Rect[] bounds = sections
                    .Select(section => BoundsRelativeTo(section, sectionGrid))
                    .ToArray();
                for (int index = 1; index < bounds.Length; index++)
                {
                    Assert.True(
                        bounds[index - 1].Bottom <= bounds[index].Top,
                        $"{sections[index - 1].Name} {bounds[index - 1]} overlaps " +
                        $"{sections[index].Name} {bounds[index]}.");
                }

                string[] sectionTitleNames =
                [
                    "ClockRecognitionGroupTitle",
                    "RegionsGroupTitle",
                    "LayoutsGroupTitle",
                    "MinimapValidationGroupTitle"
                ];
                Assert.All(sectionTitleNames, name =>
                {
                    TextBlock title = RequiredElement<TextBlock>(window, name);
                    Assert.True(title.IsVisible);
                    Assert.True(title.ActualHeight > 0);
                });

                AssertNoNegativeMargins(scrollViewer);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void MinimapControls_RenderInSeparateVerticalBands() =>
        RunOnSta(() =>
        {
            MainWindow window = CreateWindow(1280, 900);

            try
            {
                window.Show();
                window.UpdateLayout();

                FrameworkElement label =
                    RequiredElement<TextBlock>(window, "MinimapProfileLabel");
                FrameworkElement selector =
                    RequiredElement<ComboBox>(window, "MinimapProfileSelector");
                FrameworkElement status =
                    RequiredElement<Border>(window, "MinimapValidationStatusPanel");
                FrameworkElement sampleLabel =
                    RequiredElement<TextBlock>(window, "MinimapSampleLabel");
                GroupBox minimapGroup =
                    RequiredElement<GroupBox>(window, "MinimapValidationGroup");

                AssertVerticallySeparated(label, selector, minimapGroup);
                AssertVerticallySeparated(selector, status, minimapGroup);
                AssertVerticallySeparated(status, sampleLabel, minimapGroup);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void InteractiveControls_RemainHitTestableThroughScrollingAndStateChanges() =>
        RunOnSta(() =>
        {
            MainWindow window = CreateWindow(960, 700);

            try
            {
                window.Show();
                window.UpdateLayout();

                ScrollViewer scrollViewer =
                    RequiredElement<ScrollViewer>(window, "RightControlsScrollViewer");
                Assert.True(scrollViewer.ExtentHeight > scrollViewer.ViewportHeight);
                Assert.True(scrollViewer.ScrollableHeight > 0);

                string[] interactiveElementNames =
                [
                    "RecognitionEnabledToggle",
                    "EditClockRegionButton",
                    "EditMinimapRegionButton",
                    "SaveMinimapSampleButton",
                    "SaveMinimapDiagnosticButton",
                    "StartSessionRecordingButton",
                    "StopSessionRecordingButton",
                    "OpenSessionFolderButton"
                ];

                Assert.All(
                    interactiveElementNames,
                    name => AssertHitTestableAfterScrolling(
                        RequiredElement<FrameworkElement>(window, name),
                        scrollViewer,
                        window));

                MainWindowViewModel viewModel =
                    Assert.IsType<MainWindowViewModel>(window.DataContext);
                viewModel.RecognitionEnabled = !viewModel.RecognitionEnabled;
                viewModel.MinimapValidationEnabled = !viewModel.MinimapValidationEnabled;
                window.UpdateLayout();

                GroupBox[] sections =
                [
                    RequiredElement<GroupBox>(window, "ClockRecognitionGroup"),
                    RequiredElement<GroupBox>(window, "RegionsGroup"),
                    RequiredElement<GroupBox>(window, "LayoutsGroup"),
                    RequiredElement<GroupBox>(window, "MinimapValidationGroup")
                ];
                Grid sectionGrid = Assert.IsType<Grid>(
                    VisualTreeHelper.GetParent(sections[0]));
                Rect[] bounds = sections
                    .Select(section => BoundsRelativeTo(section, sectionGrid))
                    .ToArray();
                for (int index = 1; index < bounds.Length; index++)
                {
                    Assert.True(bounds[index - 1].Bottom <= bounds[index].Top);
                }

                AssertHitTestableAfterScrolling(
                    RequiredElement<Button>(window, "EditClockRegionButton"),
                    scrollViewer,
                    window);
                AssertHitTestableAfterScrolling(
                    RequiredElement<Button>(window, "SaveMinimapSampleButton"),
                    scrollViewer,
                    window);

                window.WindowState = WindowState.Maximized;
                window.UpdateLayout();
                AssertSectionsDoNotOverlap(window);

                window.WindowState = WindowState.Normal;
                window.Width = 960;
                window.Height = 700;
                window.UpdateLayout();
                AssertSectionsDoNotOverlap(window);
                Assert.True(scrollViewer.ScrollableHeight > 0);
            }
            finally
            {
                window.Close();
            }
        });

    private static void AssertSectionsDoNotOverlap(FrameworkElement window)
    {
        GroupBox[] sections =
        [
            RequiredElement<GroupBox>(window, "ClockRecognitionGroup"),
            RequiredElement<GroupBox>(window, "RegionsGroup"),
            RequiredElement<GroupBox>(window, "LayoutsGroup"),
            RequiredElement<GroupBox>(window, "MinimapValidationGroup")
        ];
        Grid sectionGrid = Assert.IsType<Grid>(VisualTreeHelper.GetParent(sections[0]));
        Rect[] bounds = sections
            .Select(section => BoundsRelativeTo(section, sectionGrid))
            .ToArray();
        for (int index = 1; index < bounds.Length; index++)
        {
            Assert.True(
                bounds[index - 1].Bottom <= bounds[index].Top,
                $"{sections[index - 1].Name} overlaps {sections[index].Name}.");
        }
    }

    private static MainWindow CreateWindow(double width, double height) =>
        new()
        {
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            ShowActivated = false
        };

    private static void AssertVerticallySeparated(
        FrameworkElement upper,
        FrameworkElement lower,
        FrameworkElement ancestor)
    {
        Rect upperBounds = BoundsRelativeTo(upper, ancestor);
        Rect lowerBounds = BoundsRelativeTo(lower, ancestor);
        Assert.True(
            upperBounds.Bottom <= lowerBounds.Top,
            $"{upper.Name} {upperBounds} overlaps {lower.Name} {lowerBounds}.");
    }

    private static void AssertHitTestableAfterScrolling(
        FrameworkElement element,
        ScrollViewer scrollViewer,
        Window window)
    {
        element.BringIntoView();
        window.UpdateLayout();

        Rect viewportBounds = BoundsRelativeTo(element, scrollViewer);
        Assert.True(
            IsInsideViewport(element, scrollViewer),
            $"{element.Name} bounds {viewportBounds} are outside the viewport.");
        Assert.True(element.IsHitTestVisible, $"{element.Name} disables hit testing.");

        Point center = element
            .TransformToAncestor(window)
            .Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        // Visual hit testing detects any element painted over the control while remaining
        // independent of command CanExecute state (several controls are disabled until capture).
        DependencyObject? hit = VisualTreeHelper.HitTest(window, center)?.VisualHit;
        Assert.True(
            hit is not null && IsAncestorOf(element, hit),
            $"{element.Name} was covered by {DescribeElement(hit)}.");
    }

    private static bool IsAncestorOf(DependencyObject ancestor, DependencyObject element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static string DescribeElement(DependencyObject? element) =>
        element is FrameworkElement frameworkElement
            ? $"{element.GetType().Name} '{frameworkElement.Name}'"
            : element?.GetType().Name ?? "nothing";

    private static void AssertNoNegativeMargins(DependencyObject root)
    {
        if (root is FrameworkElement { TemplatedParent: null } element)
        {
            Thickness margin = element.Margin;
            Assert.True(
                margin.Left >= 0 &&
                margin.Top >= 0 &&
                margin.Right >= 0 &&
                margin.Bottom >= 0,
                $"{element.GetType().Name} '{element.Name}' has negative margin {margin}.");
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            AssertNoNegativeMargins(VisualTreeHelper.GetChild(root, index));
        }
    }

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
        Assert.IsAssignableFrom<T>(root.FindName(name));

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
