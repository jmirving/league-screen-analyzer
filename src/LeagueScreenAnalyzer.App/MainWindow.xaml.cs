using System.Windows;
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
    }

    protected override async void OnClosed(EventArgs e)
    {
        await _viewModel.DisposeAsync();
        _loggerFactory.Dispose();
        base.OnClosed(e);
    }
}
