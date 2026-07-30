using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LeagueScreenAnalyzer.App.Services;
using LeagueScreenAnalyzer.App.ViewModels;
using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Capture.Windows;
using LeagueScreenAnalyzer.Imaging;
using Microsoft.Extensions.Logging;

namespace LeagueScreenAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        WindowsCaptureSessionSelector selector = new(_loggerFactory);
        CaptureController controller = new(
            selector,
            _loggerFactory.CreateLogger<CaptureController>());
        ClockProfileCatalog clockProfileCatalog = ClockProfileCatalog.CreateDefault();
        _viewModel = new MainWindowViewModel(
            controller,
            new WindowHandleProvider(this),
            Dispatcher,
            _loggerFactory.CreateLogger<MainWindowViewModel>(),
            clockProfileCatalog: clockProfileCatalog);
        DataContext = _viewModel;
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) =>
        _viewModel.SetPreviewSize(e.NewSize.Width, e.NewSize.Height);

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(PreviewSurface);
        if (_viewModel.PointerDown(point.X, point.Y))
        {
            PreviewSurface.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (PreviewSurface.IsMouseCaptured)
        {
            Point point = e.GetPosition(PreviewSurface);
            _viewModel.PointerMove(point.X, point.Y);
            e.Handled = true;
        }
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (PreviewSurface.IsMouseCaptured)
        {
            _viewModel.PointerUp();
            PreviewSurface.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CancelEdit();
            PreviewSurface.ReleaseMouseCapture();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _viewModel.DeleteSelectedRegion();
            e.Handled = true;
        }
        else if (_viewModel.IsPrecisionEditing &&
                 Keyboard.FocusedElement is not TextBoxBase &&
                 e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            int dx = e.Key == Key.Left ? -amount : e.Key == Key.Right ? amount : 0;
            int dy = e.Key == Key.Up ? -amount : e.Key == Key.Down ? amount : 0;
            bool resize = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            e.Handled = _viewModel.NudgePrecisionRegion(dx, dy, resize);
        }
    }

    private void OnPrecisionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Canvas canvas)
        {
            return;
        }

        Point point = e.GetPosition(canvas);
        if (_viewModel.PrecisionPointerDown(point.X, point.Y))
        {
            canvas.CaptureMouse();
            canvas.Focus();
            e.Handled = true;
        }
    }

    private void OnPrecisionMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Canvas canvas || !canvas.IsMouseCaptured)
        {
            return;
        }

        Point point = e.GetPosition(canvas);
        _viewModel.PrecisionPointerMove(point.X, point.Y);
        e.Handled = true;
    }

    private void OnPrecisionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Canvas canvas || !canvas.IsMouseCaptured)
        {
            return;
        }

        _viewModel.PrecisionPointerUp();
        canvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override async void OnClosed(EventArgs e)
    {
        await _viewModel.DisposeAsync();
        _loggerFactory.Dispose();
        base.OnClosed(e);
    }
}
