using System.Windows.Threading;
using CefSharp;

namespace NovaBrowser.App.Services;

public sealed class NovaLifeSpanHandler : ILifeSpanHandler
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _openInTab;
    private readonly Action<BrowserDiagnosticEvent>? _recordDiagnostic;
    private readonly Action<string>? _handleAuthCallback;

    public NovaLifeSpanHandler(
        Dispatcher dispatcher,
        Action<string> openInTab,
        Action<BrowserDiagnosticEvent>? recordDiagnostic = null,
        Action<string>? handleAuthCallback = null)
    {
        _dispatcher = dispatcher;
        _openInTab = openInTab;
        _recordDiagnostic = recordDiagnostic;
        _handleAuthCallback = handleAuthCallback;
    }

    public bool OnBeforePopup(IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser? newBrowser)
    {
        newBrowser = null;

        if (AddressParser.IsAuthCallbackUrl(targetUrl))
        {
            _recordDiagnostic?.Invoke(new BrowserDiagnosticEvent("auth-popup-callback", targetUrl, "Nova auth callback received from popup."));
            _dispatcher.BeginInvoke(() => _handleAuthCallback?.Invoke(targetUrl));
        }
        else if (IsAllowedWebUrl(targetUrl))
        {
            _recordDiagnostic?.Invoke(new BrowserDiagnosticEvent("popup-new-tab", targetUrl, $"Disposition={targetDisposition}; UserGesture={userGesture}"));
            _dispatcher.BeginInvoke(() => _openInTab(targetUrl));
        }
        else
        {
            _recordDiagnostic?.Invoke(new BrowserDiagnosticEvent("blocked-popup-protocol", targetUrl, "Blocked popup with unsupported protocol.", Blocked: true));
        }

        // Keine extra Betriebssystem-Fenster: Popups und target=_blank laufen als Nova-Tab.
        return true;
    }

    public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        return false;
    }

    public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    private static bool IsAllowedWebUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
