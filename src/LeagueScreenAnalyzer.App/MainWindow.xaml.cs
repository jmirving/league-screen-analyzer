using System.Windows;
using LeagueScreenAnalyzer.App.Services;
using LeagueScreenAnalyzer.App.ViewModels;
using LeagueScreenAnalyzer.Capture.Live;
using LeagueScreenAnalyzer.Capture.Windows;
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
        _viewModel = new MainWindowViewModel(
            controller,
            new WindowHandleProvider(this),
            Dispatcher,
            _loggerFactory.CreateLogger<MainWindowViewModel>());
        DataContext = _viewModel;
    }

    protected override async void OnClosed(EventArgs e)
    {
        await _viewModel.DisposeAsync();
        _loggerFactory.Dispose();
        base.OnClosed(e);
    }
}
