using CefSharp;
using CefSharp.Handler;

namespace NovaBrowser.App.Services;

public sealed record BrowserDiagnosticEvent(
    string Kind,
    string? Url,
    string Details,
    bool Blocked = false,
    bool Failed = false,
    bool Media = false,
    int HttpStatus = 0);

public sealed class NovaVideoCompatibilityRequestHandler : RequestHandler
{
    private readonly Action<string> _showStatus;
    private readonly Func<string?, bool> _isProtectionDisabled;
    private readonly Func<bool> _isHttpsOnlyEnabled;
    private readonly Action<BrowserDiagnosticEvent> _recordDiagnostic;
    private readonly Action<string> _handleAuthCallback;
    private readonly Action<string?, string> _showCrashPage;
    private readonly NovaVideoResourceRequestHandler _resourceHandler;

    public NovaVideoCompatibilityRequestHandler(
        Action<string> showStatus,
        Func<string?, bool> isProtectionDisabled,
        Func<bool> isHttpsOnlyEnabled,
        Action<BrowserDiagnosticEvent> recordDiagnostic,
        Action<string> handleAuthCallback,
        Action<string?, string> showCrashPage)
    {
        _showStatus = showStatus;
        _isProtectionDisabled = isProtectionDisabled;
        _isHttpsOnlyEnabled = isHttpsOnlyEnabled;
        _recordDiagnostic = recordDiagnostic;
        _handleAuthCallback = handleAuthCallback;
        _showCrashPage = showCrashPage;
        _resourceHandler = new NovaVideoResourceRequestHandler(showStatus, isProtectionDisabled, recordDiagnostic);
    }

    protected override bool OnBeforeBrowse(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        bool userGesture,
        bool isRedirect)
    {
        if (AddressParser.IsAuthCallbackUrl(request.Url))
        {
            _recordDiagnostic(new BrowserDiagnosticEvent("auth-callback", request.Url, "Nova auth callback received."));
            WriteDiagnostic("auth-callback", request.Url, "Nova auth callback received.");
            _handleAuthCallback(request.Url);
            return true;
        }

        if (_isHttpsOnlyEnabled() &&
            frame.IsMain &&
            Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = -1
            };

            var upgradedUrl = builder.Uri.ToString();
            _recordDiagnostic(new BrowserDiagnosticEvent("https-only-upgrade", request.Url, $"Redirected to {upgradedUrl}."));
            WriteDiagnostic("https-only-upgrade", request.Url, $"Redirected to {upgradedUrl}.");
            browser.MainFrame.LoadUrl(upgradedUrl);
            return true;
        }

        if (!IsAllowedWebOrInternalUrl(request.Url))
        {
            _recordDiagnostic(new BrowserDiagnosticEvent("blocked-protocol", request.Url, "Blocked non-web navigation protocol.", Blocked: true));
            WriteDiagnostic("blocked-protocol", request.Url, "Blocked non-web navigation protocol.");
            return true;
        }

        _recordDiagnostic(new BrowserDiagnosticEvent(isRedirect ? "redirect" : "navigate", request.Url, $"UserGesture={userGesture}"));
        WriteDiagnostic(isRedirect ? "redirect" : "navigate", request.Url, $"UserGesture={userGesture}");
        return false;
    }

    protected override IResourceRequestHandler? GetResourceRequestHandler(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        bool isNavigation,
        bool isDownload,
        string requestInitiator,
        ref bool disableDefaultHandling)
    {
        return IsLikelyVideoPage(request.ReferrerUrl) ||
               IsLikelyVideoPage(frame.Url) ||
               IsTargetVideoDomain(request.Url)
            ? _resourceHandler
            : null;
    }

    protected override void OnRenderProcessTerminated(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefTerminationStatus status,
        int errorCode,
        string errorString)
    {
        var url = browser.MainFrame?.Url ?? chromiumWebBrowser.Address;
        var details = $"Status={status}; ErrorCode={errorCode}; Error={errorString}";
        _recordDiagnostic(new BrowserDiagnosticEvent("render-process-terminated", url, details, Failed: true));
        WriteDiagnostic("render-process-terminated", url, details);

        try
        {
            _showStatus("Eine Webseite ist abgestuerzt. Nova bleibt offen; lade den Tab bei Bedarf neu.");
            _showCrashPage(url, details);
        }
        catch (Exception ex)
        {
            NovaBrowser.App.App.LogException("render-process-status", ex);
        }
    }

    public static bool IsLikelyVideoPage(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.Contains("youtube.") ||
               host.Contains("youtu.be") ||
               host.Contains("pornhub.") ||
               host.Contains("xhamster.") ||
               host.Contains("xhcdn.") ||
               host.Contains("vimeo.") ||
               host.Contains("twitch.") ||
               host.Contains("dailymotion.");
    }

    public static bool NeedsCompatibilityMode(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.Contains("pornhub.") ||
               host.Contains("xhamster.");
    }

    public static bool IsTargetVideoDomain(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.Contains("pornhub.") ||
               host.Contains("phncdn.com") ||
               host.Contains("pornhubpremium.com") ||
               host.Contains("xhamster.") ||
               host.Contains("xhcdn.") ||
               host.Contains("cdn77.org") ||
               host.Contains("trafficjunky.net") ||
               host.Contains("jwpcdn.com") ||
               host.Contains("jwpin.com");
    }

    private sealed class NovaVideoResourceRequestHandler : ResourceRequestHandler
    {
        private static readonly string[] BlockedVideoNoiseHosts =
        {
            "taboola.com",
            "outbrain.com",
            "trafficjunky.net",
            "exoclick.com",
            "juicyads.com",
            "popads.net",
            "popcash.net"
        };

        private int _blockedCount;
        private readonly Action<string> _showStatus;
        private readonly Func<string?, bool> _isProtectionDisabled;
        private readonly Action<BrowserDiagnosticEvent> _recordDiagnostic;

        public NovaVideoResourceRequestHandler(
            Action<string> showStatus,
            Func<string?, bool> isProtectionDisabled,
            Action<BrowserDiagnosticEvent> recordDiagnostic)
        {
            _showStatus = showStatus;
            _isProtectionDisabled = isProtectionDisabled;
            _recordDiagnostic = recordDiagnostic;
        }

        protected override CefReturnValue OnBeforeResourceLoad(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            IFrame frame,
            IRequest request,
            IRequestCallback callback)
        {
            _recordDiagnostic(new BrowserDiagnosticEvent("request", request.Url, "Resource requested.", Media: IsMediaUrl(request.Url)));
            if (IsMediaUrl(request.Url))
            {
                _recordDiagnostic(new BrowserDiagnosticEvent("media-request", request.Url, "Media/CDN request allowed.", Media: true));
                WriteDiagnostic("media-request", request.Url, "Media/CDN request allowed.");
            }

            if (!_isProtectionDisabled(request.Url) && !IsTargetVideoDomain(request.Url) && ShouldBlockVideoNoise(request.Url))
            {
                var blocked = Interlocked.Increment(ref _blockedCount);
                if (blocked is 1 or 5 or 15)
                {
                    _showStatus($"Video-Kompatibilitaetsmodus: {blocked} schwere Werbe-/Tracker-Anfragen erkannt.");
                }

                _recordDiagnostic(new BrowserDiagnosticEvent("observed-noise-request", request.Url, "Allowed known popup/ad host to avoid anti-adblock/player breakage."));
                WriteDiagnostic("observed-noise-request", request.Url, "Allowed known popup/ad host to avoid anti-adblock/player breakage.");
            }

            return CefReturnValue.Continue;
        }

        private static bool ShouldBlockVideoNoise(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (IsMediaOrCoreResource(uri.AbsolutePath))
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();
            return BlockedVideoNoiseHosts.Any(blockedHost => host.Equals(blockedHost, StringComparison.OrdinalIgnoreCase) ||
                                                             host.EndsWith("." + blockedHost, StringComparison.OrdinalIgnoreCase));
        }

        protected override void OnResourceLoadComplete(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            IFrame frame,
            IRequest request,
            IResponse response,
            UrlRequestStatus status,
            long receivedContentLength)
        {
            if ((status != UrlRequestStatus.Success && response.StatusCode != 206) || response.StatusCode >= 400)
            {
                _recordDiagnostic(new BrowserDiagnosticEvent(
                    "resource-complete",
                    request.Url,
                    $"Status={status}; Http={response.StatusCode}; Bytes={receivedContentLength}",
                    Failed: true,
                    Media: IsMediaUrl(request.Url),
                    HttpStatus: response.StatusCode));
                WriteDiagnostic("resource-complete", request.Url, $"Status={status}; Http={response.StatusCode}; Bytes={receivedContentLength}");
            }
        }
    }

    private static bool IsAllowedWebOrInternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("nova://", StringComparison.OrdinalIgnoreCase) ||
            IsAllowedNovaAddonFile(url))
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsAllowedNovaAddonFile(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return false;
        }

        try
        {
            var fullPath = System.IO.Path.GetFullPath(uri.LocalPath);
            var roots = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "WebseiteAddon"),
                System.IO.Path.Combine(Environment.CurrentDirectory, "Assets", "WebseiteAddon"),
                System.IO.Path.Combine(Environment.CurrentDirectory, "NovaBrowser.CefSharp", "src", "NovaBrowser.CefSharp", "Assets", "WebseiteAddon")
            };

            foreach (var root in roots)
            {
                var fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                if (fullPath.StartsWith(fullRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsMediaOrCoreResource(string path)
    {
        return path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMediaUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteDiagnostic(string kind, string? url, string details)
    {
        try
        {
            var diagnosticsDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NovaBrowser.CefSharp");
            System.IO.Directory.CreateDirectory(diagnosticsDir);
            var line = $"{DateTimeOffset.Now:u}\t{kind}\t{details}\tUrl={url}{Environment.NewLine}";
            System.IO.File.AppendAllText(System.IO.Path.Combine(diagnosticsDir, "browser-diagnostics.log"), line);
        }
        catch
        {
            // Logging darf die Webseite niemals beeinflussen.
        }
    }
}
