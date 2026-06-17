using System.Windows.Threading;
using CefSharp;
using CefSharp.Handler;

namespace NovaBrowser.App.Services;

public sealed class NovaDisplayHandler : DisplayHandler
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<bool> _setFullscreen;

    public NovaDisplayHandler(Dispatcher dispatcher, Action<bool> setFullscreen)
    {
        _dispatcher = dispatcher;
        _setFullscreen = setFullscreen;
    }

    protected override void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen)
    {
        _dispatcher.BeginInvoke(() => _setFullscreen(fullscreen));
    }
}
