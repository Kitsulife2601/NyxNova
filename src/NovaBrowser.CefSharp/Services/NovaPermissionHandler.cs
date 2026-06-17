using CefSharp;
using CefSharp.Handler;

namespace NovaBrowser.App.Services;

public sealed class NovaPermissionHandler : PermissionHandler
{
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private readonly Action<BrowserDiagnosticEvent> _recordDiagnostic;
    private readonly Func<string, string, string, bool> _showPermissionPrompt;

    public NovaPermissionHandler(
        System.Windows.Threading.Dispatcher dispatcher,
        Action<BrowserDiagnosticEvent> recordDiagnostic,
        Func<string, string, string, bool> showPermissionPrompt)
    {
        _dispatcher = dispatcher;
        _recordDiagnostic = recordDiagnostic;
        _showPermissionPrompt = showPermissionPrompt;
    }

    protected override bool OnRequestMediaAccessPermission(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string requestingOrigin,
        MediaAccessPermissionType requestedPermissions,
        IMediaAccessCallback callback)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            var allow = _showPermissionPrompt(
                requestingOrigin,
                BuildMediaTitle(requestedPermissions),
                BuildMediaMessage(requestedPermissions));

            _recordDiagnostic(new BrowserDiagnosticEvent(
                allow ? "permission-allowed" : "permission-denied",
                requestingOrigin,
                $"Media permission: {requestedPermissions}"));

            if (allow)
            {
                callback.Continue(requestedPermissions);
            }
            else
            {
                callback.Cancel();
            }
        });

        return true;
    }

    protected override bool OnShowPermissionPrompt(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        ulong promptId,
        string requestingOrigin,
        PermissionRequestType requestedPermissions,
        IPermissionPromptCallback callback)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            var allow = _showPermissionPrompt(
                requestingOrigin,
                BuildPermissionTitle(requestedPermissions),
                $"Diese Seite moechte folgende Berechtigung nutzen:\n{requestedPermissions}");

            _recordDiagnostic(new BrowserDiagnosticEvent(
                allow ? "permission-allowed" : "permission-denied",
                requestingOrigin,
                $"Prompt permission: {requestedPermissions}"));

            callback.Continue(allow ? PermissionRequestResult.Accept : PermissionRequestResult.Deny);
        });

        return true;
    }

    protected override void OnDismissPermissionPrompt(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        ulong promptId,
        PermissionRequestResult result)
    {
        _recordDiagnostic(new BrowserDiagnosticEvent("permission-dismissed", browser.MainFrame?.Url, $"Result={result}"));
    }

    private static string BuildMediaTitle(MediaAccessPermissionType permissions)
    {
        var wantsCamera = permissions.HasFlag(MediaAccessPermissionType.VideoCapture) ||
                          permissions.HasFlag(MediaAccessPermissionType.DesktopVideoCapture);
        var wantsMic = permissions.HasFlag(MediaAccessPermissionType.AudioCapture) ||
                       permissions.HasFlag(MediaAccessPermissionType.DesktopAudioCapture);

        return wantsCamera && wantsMic
            ? "Kamera und Mikrofon erlauben?"
            : wantsCamera
                ? "Kamera erlauben?"
                : wantsMic
                    ? "Mikrofon erlauben?"
                    : "Medienzugriff erlauben?";
    }

    private static string BuildMediaMessage(MediaAccessPermissionType permissions)
    {
        var parts = new List<string>();
        if (permissions.HasFlag(MediaAccessPermissionType.VideoCapture))
        {
            parts.Add("Kamera");
        }

        if (permissions.HasFlag(MediaAccessPermissionType.AudioCapture))
        {
            parts.Add("Mikrofon");
        }

        if (permissions.HasFlag(MediaAccessPermissionType.DesktopVideoCapture))
        {
            parts.Add("Bildschirmaufnahme");
        }

        if (permissions.HasFlag(MediaAccessPermissionType.DesktopAudioCapture))
        {
            parts.Add("System-Audio");
        }

        return parts.Count == 0
            ? $"Diese Seite moechte Medienzugriff nutzen: {permissions}"
            : "Diese Seite moechte Zugriff auf " + string.Join(", ", parts) + ".";
    }

    private static string BuildPermissionTitle(PermissionRequestType permissions)
    {
        var text = permissions.ToString();
        return text.Contains("Notification", StringComparison.OrdinalIgnoreCase)
            ? "Benachrichtigungen erlauben?"
            : "Berechtigung erlauben?";
    }

}
