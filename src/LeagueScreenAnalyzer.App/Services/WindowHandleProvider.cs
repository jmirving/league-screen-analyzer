using System.Windows;
using System.Windows.Interop;

namespace LeagueScreenAnalyzer.App.Services;

public interface IWindowHandleProvider
{
    nint GetHandle();
}

public sealed class WindowHandleProvider(Window window) : IWindowHandleProvider
{
    private readonly Window _window = window ?? throw new ArgumentNullException(nameof(window));

    public nint GetHandle()
    {
        nint handle = new WindowInteropHelper(_window).Handle;
        if (handle == 0)
        {
            throw new InvalidOperationException("The application window handle is not available yet.");
        }

        return handle;
    }
}
