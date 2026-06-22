using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using CefSharp;
using CefSharp.DevTools.Page;
using Microsoft.Win32;
using Velopack;
using Velopack.Sources;
using NovaBrowser.App.Browser;
using NovaBrowser.App.Models;
using NovaBrowser.App.Services;
using NovaBrowser.App.UI;
using ChromiumWebBrowser = CefSharp.Wpf.HwndHost.ChromiumWebBrowser;
using NovaDownloadItem = NovaBrowser.App.Models.DownloadItem;

namespace NovaBrowser.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string DarkBlankPage = "data:text/html;charset=utf-8,%3C!doctype%20html%3E%3Chtml%20style%3D%22background%3A%230b0911%22%3E%3Cbody%20style%3D%22margin%3A0%3Bbackground%3A%230b0911%22%3E%3C%2Fbody%3E%3C%2Fhtml%3E";

    private static readonly string[] CommonTrackerHosts =
    {
        "doubleclick.net",
        "googlesyndication.com",
        "google-analytics.com",
        "facebook.com",
        "scorecardresearch.com",
        "hotjar.com",
        "segment.com"
    };

    private readonly BookmarkService _bookmarkService = new();
    private readonly HistoryService _historyService = new();
    private readonly DownloadService _downloadService = new();
    private readonly SettingsService _settingsService = new();
    private readonly AddonService _addonService = new();
    private readonly TelegramBotService _telegramBotService = new();
    private readonly JsonStore<SessionState> _sessionStore = new("sessions.json");
    private readonly Dictionary<BrowserTab, DateTimeOffset> _tabLastActiveAt = new();
    private readonly Dictionary<BrowserTab, string> _snoozedTabUrls = new();
    private readonly System.Windows.Threading.DispatcherTimer _ecoTimer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };
    private BrowserTab? _activeTab;
    private AddonItem? _selectedAddon;
    private AddonItem? _activeDetailAddon;
    private ChromiumWebBrowser? _mediaProbeBrowser;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private bool _isFullscreen;
    private bool _isClosing;
    private Storyboard? _reloadStoryboard;
    private Storyboard? _loadingBarStoryboard;
    private Storyboard? _pageLoadingStoryboard;
    private Storyboard? _downloadPulseStoryboard;
    private Window? _downloadsFlyoutWindow;
    private Window? _bookmarkFlyoutWindow;
    private readonly System.Windows.Threading.DispatcherTimer _updateTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(650)
    };
    private readonly System.Windows.Threading.DispatcherTimer _googleAuthTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };
    private double _updateProgress;
    private readonly bool _isPrivateWindow;
    private const string AddonSiteAccessSettingsKey = "site-access";
    private const string ProtectionDisabledDomainsSettingsKey = "protection-disabled-domains";
    private int _findRequestId = 1;
    private readonly BrowserDiagnosticsState _diagnostics = new();
    private readonly UpdateManager _updateManager = new(new GithubSource("https://github.com/Kitsulife2601/NyxNova", null, prerelease: true, downloader: null));
    private UpdateInfo? _availableUpdate;
    private VelopackAsset? _readyUpdate;
    private bool _suppressOmniboxSuggestions;
    private bool _isApplyingOmniboxAutocomplete;
    private BrowserTab? _tabDragCandidate;
    private Point _tabDragStartPoint;
    private bool _googleAuthCheckRunning;
    private bool? _lastGoogleSignedIn;
    private string? _lastGoogleAuthProbeUrl;
    private bool _windowStateTransitioning;
    private TelegramBotSnapshot? _lastTelegramBotSnapshot;
    private byte[]? _lastTelegramBotScreenshot;

    public MainWindow() : this(false)
    {
    }

    private MainWindow(bool isPrivateWindow)
    {
        _isPrivateWindow = isPrivateWindow;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyRoundedCorners();
        DataContext = this;
        LoadServices();
        ConfigureLocalOnlyFeatures();
        NovaThemeService.Apply(_settingsService.Current.Theme, Resources);
        UpdateThemeCardStatus(_settingsService.Current.Theme);
        Downloads.CollectionChanged += Downloads_CollectionChanged;
        foreach (var item in Downloads.OfType<INotifyPropertyChanged>())
        {
            item.PropertyChanged += DownloadItem_PropertyChanged;
        }
        UpdateDownloadVisualState();
        if (_isPrivateWindow)
        {
            CreateTab(AddressParser.HomeUrl, true);
            StatusText.Text = "Privates Fenster: Sitzung wird nicht wiederhergestellt.";
        }
        else if (!string.IsNullOrWhiteSpace(App.StartupUrl))
        {
            CreateTab(App.StartupUrl, true);
        }
        else if (!_settingsService.Current.SmartSessionRestoreEnabled)
        {
            CreateTab(AddressParser.HomeUrl, true);
        }
        else
        {
            RestoreSession();
        }

        MenuPanel.ZoomText = $"{Math.Round(100 * Math.Pow(1.2, _settingsService.Current.ZoomLevel))} %";
        UpdateWindowChromeState();
        _updateTimer.Tick += UpdateTimer_Tick;
        _ecoTimer.Tick += EcoTimer_Tick;
        _ecoTimer.Start();
        _googleAuthTimer.Tick += GoogleAuthTimer_Tick;
        _googleAuthTimer.Start();
        ShowBetaUpdateNotice();
    }

    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    public ObservableCollection<Bookmark> Bookmarks => _bookmarkService.Items;
    public ObservableCollection<HistoryItem> History => _historyService.Items;
    public ObservableCollection<NovaDownloadItem> Downloads => _downloadService.Items;
    public ObservableCollection<AddonItem> StoreAddons => _addonService.Catalog;
    public ObservableCollection<AddonItem> InstalledAddons => _addonService.Installed;
    public ObservableCollection<OmniboxSuggestion> OmniboxSuggestions { get; } = new();
    public ObservableCollection<TelegramBotChange> TelegramBotChanges { get; } = new();
    public IEnumerable<AddonItem> PinnedExtensions => _addonService.Pinned.ToList();
    public IEnumerable<HistoryItem> RecentHistory => History.Take(5).ToList();
    public IEnumerable<NovaDownloadItem> RecentDownloads => Downloads.Take(5).ToList();
    public string CurrentExtensionAccessText { get; private set; } = "Addons auf dieser Seite erlauben";
    public string CurrentExtensionAccessHint { get; private set; } = "Aktuelle Seite: nova://start";
    public Brush ExtensionAccessToggleBrush => ExtensionsAllowedForCurrentSite
        ? new SolidColorBrush(Color.FromRgb(169, 104, 255))
        : new SolidColorBrush(Color.FromRgb(61, 45, 80));
    public HorizontalAlignment ExtensionAccessKnobAlignment => ExtensionsAllowedForCurrentSite ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public bool ExtensionsAllowedForCurrentSite { get; private set; } = true;
    public string UpdateStatusText { get; private set; } = "Beta-Update verfuegbar";
    public string UpdateDetailText { get; private set; } = "NyxNova Beta kann im Hintergrund vorbereitet werden.";
    public string UpdateActionText { get; private set; } = "Update pruefen";
    public double UpdateProgress
    {
        get => _updateProgress;
        private set
        {
            _updateProgress = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdateProgressText));
        }
    }

    public string UpdateProgressText => $"{Math.Round(UpdateProgress)} %";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void LoadServices()
    {
        _settingsService.Load();
        _bookmarkService.Load();
        _historyService.Load();
        _downloadService.Load();
        _addonService.Load();

        SearchEngineBox.SelectedIndex = (int)_settingsService.Current.SearchEngine;
        SettingsSearchEngineBox.SelectedIndex = (int)_settingsService.Current.SearchEngine;
        BookmarkBarCheckBox.IsChecked = _settingsService.Current.ShowBookmarkBar;
        TrackerBlockerCheckBox.IsChecked = _settingsService.Current.TrackerBlockerEnabled;
        AggressiveTrackerBlockerCheckBox.IsChecked = _settingsService.Current.AggressiveTrackerBlockerEnabled;
        HttpsOnlyModeCheckBox.IsChecked = _settingsService.Current.HttpsOnlyModeEnabled;
        TabSleepCheckBox.IsChecked = _settingsService.Current.TabSleepEnabled;
        HardwareAccelerationCheckBox.IsChecked = _settingsService.Current.HardwareAccelerationEnabled;
        EcoModeCheckBox.IsChecked = _settingsService.Current.EcoModeEnabled;
        SmartSessionRestoreCheckBox.IsChecked = _settingsService.Current.SmartSessionRestoreEnabled;
        GlobalFingerprintingCheckBox.IsChecked = _settingsService.Current.GlobalFingerprintingProtectionEnabled;
        AutomaticProtectionCheckBox.IsChecked = _settingsService.Current.AutomaticProtectionEnabled;
        DarkModeEnforcerCheckBox.IsChecked = _settingsService.Current.DarkModeEnforcerEnabled;
        LazyMediaLoadingCheckBox.IsChecked = _settingsService.Current.LazyMediaLoadingEnabled;
        TelegramBotChatIdBox.Text = _settingsService.Current.TelegramBotChatId;
        TelegramBotEnabledCheckBox.IsChecked = _settingsService.Current.TelegramBotEnabled;
        TelegramBotStatusText.Text = _telegramBotService.HasStoredToken
            ? "Token lokal verschluesselt gespeichert."
            : "Noch kein Bot Token gespeichert.";
        UpdateBookmarkBarVisibility();
        UpdateBuildInfo();
        NotifyCollectionViews();
    }

    private void ConfigureLocalOnlyFeatures()
    {
        if (IsPackagedInstall())
        {
            TelegramBotButton.Visibility = Visibility.Collapsed;
            TelegramBotPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static bool IsPackagedInstall()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            var appRoot = string.IsNullOrWhiteSpace(exePath) ? null : System.IO.Path.GetDirectoryName(exePath);
            return !string.IsNullOrWhiteSpace(appRoot) && System.IO.File.Exists(System.IO.Path.Combine(appRoot, "Update.exe"));
        }
        catch
        {
            return false;
        }
    }

    private void UpdateBuildInfo()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "unbekannt";
            var exePath = Environment.ProcessPath ?? assembly.Location;
            var buildTime = System.IO.File.Exists(exePath)
                ? System.IO.File.GetLastWriteTime(exePath).ToString("dd.MM.yyyy HH:mm:ss")
                : "unbekannt";
            BuildInfoText.Text = $"Version: {version}\nBuild-Zeit: {buildTime}\nEXE: {exePath}";
        }
        catch
        {
            BuildInfoText.Text = "Build-Version konnte nicht geladen werden.";
        }
    }

    private void ShowBetaUpdateNotice()
    {
        if (_isPrivateWindow)
        {
            return;
        }

        BetaUpdateBanner.Visibility = Visibility.Visible;
    }

    private void SetUpdateText(string status, string detail)
    {
        UpdateStatusText = status;
        UpdateDetailText = detail;
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(UpdateDetailText));
    }

    private void SetUpdateAction(string text)
    {
        UpdateActionText = text;
        OnPropertyChanged(nameof(UpdateActionText));
    }

    private void OpenUpdatePage_Click(object sender, RoutedEventArgs e)
    {
        NavigateActive("nova://update");
    }

    private void HideUpdatePage_Click(object sender, RoutedEventArgs e)
    {
        NavigateActive(AddressParser.HomeUrl);
    }

    private void DismissUpdateBanner_Click(object sender, RoutedEventArgs e)
    {
        BetaUpdateBanner.Visibility = Visibility.Collapsed;
    }

    private async void StartBetaUpdate_Click(object sender, RoutedEventArgs e)
    {
        BetaUpdateBanner.Visibility = Visibility.Collapsed;

        try
        {
            if (_readyUpdate is not null || _updateManager.UpdatePendingRestart is not null)
            {
                SetUpdateText("Update wird installiert", "NyxNova startet gleich neu und uebernimmt die geladene Version.");
                SetUpdateAction("Installiere...");
                _updateManager.ApplyUpdatesAndRestart(_readyUpdate ?? _updateManager.UpdatePendingRestart);
                return;
            }

            NavigateActive("nova://update");
            UpdateProgress = 0;
            SetUpdateAction("Pruefe...");
            SetUpdateText("Update wird geprueft", "NyxNova sucht auf GitHub nach einer neuen Beta-Version.");

            if (!_updateManager.IsInstalled)
            {
                SetUpdateAction("Installer noetig");
                SetUpdateText("Installer erforderlich", "Automatische Updates funktionieren erst, wenn NyxNova ueber den Setup-Installer installiert wurde. Die portable EXE kann Updates nur anzeigen, nicht selbst anwenden.");
                return;
            }

            _availableUpdate = await _updateManager.CheckForUpdatesAsync();
            if (_availableUpdate is null)
            {
                UpdateProgress = 100;
                SetUpdateAction("Erneut pruefen");
                SetUpdateText("NyxNova ist aktuell", "Es wurde kein neueres GitHub-Release gefunden.");
                return;
            }

            var target = _availableUpdate.TargetFullRelease;
            SetUpdateAction("Laedt...");
            SetUpdateText("Update gefunden", $"Version {target.Version} wird im Hintergrund heruntergeladen.");

            await _updateManager.DownloadUpdatesAsync(_availableUpdate, progress =>
            {
                Dispatcher.Invoke(() => UpdateProgress = progress);
            });

            _readyUpdate = target;
            UpdateProgress = 100;
            SetUpdateAction("Neu starten und installieren");
            SetUpdateText("Update bereit", "Das Update wurde heruntergeladen. Beim Neustart wird es wie bei einem normalen Browser-Update uebernommen.");
        }
        catch (Exception ex)
        {
            App.LogException("update-check-download", ex);
            SetUpdateAction("Erneut versuchen");
            SetUpdateText("Update konnte nicht geladen werden", "Bitte pruefe, ob auf GitHub ein Velopack-Release vorhanden ist. Details stehen im Nova-Log.");
        }
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        UpdateProgress += UpdateProgress < 70 ? 7 : 3;

        if (UpdateProgress < 35)
        {
            SetUpdateText("Beta-Update wird vorbereitet", "Release-Informationen werden geprueft und der Download wird vorbereitet.");
        }
        else if (UpdateProgress < 75)
        {
            SetUpdateText("Beta-Update laeuft im Hintergrund", "Das Beta-Paket wird heruntergeladen. Nova bleibt waehrenddessen nutzbar.");
        }
        else if (UpdateProgress < 100)
        {
            SetUpdateText("Beta-Update wird fertiggestellt", "Dateien werden geprueft. Danach kann die neue Version gestartet werden.");
        }
        else
        {
            _updateTimer.Stop();
            UpdateProgress = 100;
            SetUpdateText("Beta-Update bereit", "Das Beta-Paket ist vorbereitet. Sobald GitHub verbunden ist, wird hier der echte Release-Download genutzt.");
        }
    }

    private void RestoreSession()
    {
        var session = _sessionStore.Load();
        var urls = session.OpenTabs.Count == 0
            ? new List<string> { AddressParser.HomeUrl }
            : session.OpenTabs.Where(url => AddressParser.IsInternalUrl(url) || AddressParser.IsWebUrl(url)).Take(12).ToList();

        foreach (var url in urls)
        {
            CreateTab(url, select: false);
        }

        var active = Tabs.FirstOrDefault(tab => tab.Url == session.ActiveTabUrl) ?? Tabs.FirstOrDefault();
        if (active is not null)
        {
            SelectTab(active);
        }
    }

    private BrowserTab CreateTab(string rawUrl, bool select)
    {
        var url = ApplyNavigationSettings(AddressParser.Normalize(rawUrl, _settingsService.Current.SearchEngine));
        var tab = new BrowserTab(null, url)
        {
            Title = GetInternalTitle(url)
        };

        Tabs.Add(tab);
        _tabLastActiveAt[tab] = DateTimeOffset.UtcNow;
        if (select)
        {
            SelectTab(tab);
        }

        return tab;
    }

    private ChromiumWebBrowser EnsureBrowser(BrowserTab tab)
    {
        if (tab.Browser is not null)
        {
            return tab.Browser;
        }

        var browserSettings = new CefSharp.BrowserSettings
        {
            Javascript = CefState.Enabled,
            JavascriptCloseWindows = CefState.Disabled,
            LocalStorage = CefState.Enabled,
            Databases = CefState.Enabled,
            WebGl = CefState.Enabled,
            ImageLoading = CefState.Enabled,
            WindowlessFrameRate = 60,
            BackgroundColor = Cef.ColorSetARGB(255, 11, 9, 17)
        };

        var browser = new ChromiumWebBrowser(DarkBlankPage)
        {
            BrowserSettings = browserSettings,
            LifeSpanHandler = new NovaLifeSpanHandler(Dispatcher, OpenInNewTab, RecordDiagnostic, HandleAuthCallback),
            DownloadHandler = new NovaDownloadHandler(_downloadService, Dispatcher),
            RequestHandler = new NovaVideoCompatibilityRequestHandler(
                message => RunOnUi(() => StatusText.Text = message, "request-status"),
                IsProtectionDisabledForUrl,
                () => _settingsService.Current.HttpsOnlyModeEnabled,
                RecordDiagnostic,
                HandleAuthCallback,
                ShowCrashErrorPage),
            PermissionHandler = new NovaPermissionHandler(Dispatcher, RecordDiagnostic, ShowBrowserPermissionPrompt),
            MenuHandler = new NovaContextMenuHandler((link, image, page) => RunOnUi(() => ShowWebContextMenu(link, image, page), "web-context-menu")),
            JsDialogHandler = new NovaJsDialogHandler(message => RunOnUi(() => StatusText.Text = message, "js-dialog-status")),
            DisplayHandler = new NovaDisplayHandler(Dispatcher, SetFullscreenFromWebContent)
        };
        var slowLoadTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        slowLoadTimer.Tick += (_, _) =>
        {
            slowLoadTimer.Stop();
            if (ReferenceEquals(tab, _activeTab) && browser.IsLoading && AddressParser.IsWebUrl(tab.Url))
            {
                if (NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(tab.Url))
                {
                    RecordDiagnostic(new BrowserDiagnosticEvent(
                        "slow-video-page",
                        tab.Url,
                        "Video-Seite laedt lange. Nova zeigt den Hinweis, stoppt den Player aber nicht hart.",
                        Media: true));
                    ShowSlowLoadBar(tab.Url, "Diese Video-Seite laedt lange. Nova stoppt den Player nicht mehr automatisch, damit Quellen/Fallbacks fertig laden koennen.");
                    return;
                }

                StopStuckLoad(
                    browser,
                    tab,
                    "slow-load-timeout",
                    "Diese Seite hat den Ladevorgang nicht sauber beendet. Nova hat das endlose Laden gestoppt; die geladene Seite bleibt bedienbar.");
            }
        };
        var videoLoadTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(14)
        };
        videoLoadTimer.Tick += (_, _) =>
        {
            videoLoadTimer.Stop();
            if (ReferenceEquals(tab, _activeTab) &&
                browser.IsLoading &&
                NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(tab.Url))
            {
                StatusText.Text = "Video-Kompatibilitaetsmodus aktiv: Nova prueft WebM-Fallback, falls MP4/H.264 haengt.";
                _ = TryApplyWebmFallbackAsync(browser, tab);
            }
        };

        DependencyPropertyDescriptor
            .FromProperty(ChromiumWebBrowser.AddressProperty, typeof(ChromiumWebBrowser))
            .AddValueChanged(browser, (_, _) => RunOnUi(() =>
        {
            var address = browser.Address ?? "";
            if (address.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            tab.Url = address;
            if (ReferenceEquals(tab, _activeTab))
            {
                AddressBox.Text = tab.Url;
                UpdateExtensionAccessState();
            }

            CheckGoogleBlockedAddress(address);
            QueueGoogleAuthCheck(address);
        }, "browser-address-changed"));

        browser.TitleChanged += (_, args) =>
        {
            var title = args.NewValue as string ?? "Neuer Tab";
            RunOnUi(() =>
            {
                tab.Title = title;
                if (ReferenceEquals(tab, _activeTab))
                {
                    StatusText.Text = tab.Title;
                }
            }, "browser-title-changed");
        };

        browser.ConsoleMessage += (_, args) =>
        {
            var source = args.Source;
            var line = args.Line;
            var message = args.Message;
            RunOnUi(() =>
            {
                if (!ReferenceEquals(tab, _activeTab))
                {
                    return;
                }

                if (NovaVideoCompatibilityRequestHandler.IsLikelyVideoPage(tab.Url))
                {
                    RecordDiagnostic(new BrowserDiagnosticEvent("console", tab.Url, $"{source}:{line} {message}"));
                    WriteBrowserDiagnostic("console", tab.Url, $"{source}:{line} {message}");
                }

                if (IsMediaConsoleMessage(message))
                {
                    RecordDiagnostic(new BrowserDiagnosticEvent("media-console", tab.Url, message, Failed: true, Media: true));
                    StatusText.Text = "Video/Audio-Hinweis: Der Stream nutzt wahrscheinlich ein Format, das CEF nur eingeschraenkt unterstuetzt.";
                    if (IsFatalMediaConsoleMessage(message))
                    {
                        ShowSlowLoadBar(tab.Url, BuildMediaErrorMessage(message));
                        WriteBrowserDiagnostic("media-error", tab.Url, message);
                    }
                }
            }, "browser-console-message");
        };

        browser.LoadingStateChanged += (_, args) =>
        {
            var isLoading = args.IsLoading;
            RunOnUi(() =>
            {
                if (!ReferenceEquals(tab, _activeTab))
                {
                    return;
                }

                SetLoadingAnimation(isLoading);
                StatusText.Text = isLoading ? "Laedt..." : "Bereit";
                if (isLoading && AddressParser.IsWebUrl(tab.Url))
                {
                    if (NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(tab.Url))
                    {
                        EnsureProtectionDisabledForDomain(tab.Url);
                        _ = CheckTargetVideoDomainStateFromBrowserAsync(browser, tab);
                    }

                    _diagnostics.Start(tab.Url);
                    RefreshDiagnosticsPage();
                    SlowLoadBar.Visibility = Visibility.Collapsed;
                    slowLoadTimer.Stop();
                    slowLoadTimer.Start();
                }
                else
                {
                    slowLoadTimer.Stop();
                }

                if (isLoading &&
                    NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(tab.Url))
                {
                    videoLoadTimer.Stop();
                    videoLoadTimer.Start();
                }
                else
                {
                    videoLoadTimer.Stop();
                }

                if (!isLoading && AddressParser.IsWebUrl(tab.Url))
                {
                    browser.SetZoomLevel(_settingsService.Current.ZoomLevel);
                    _diagnostics.Stop();
                    RefreshDiagnosticsPage();
                    _historyService.Record(tab.Title, tab.Url);
                    NotifyCollectionViews();
                    UpdateBookmarkButton();
                    if (_settingsService.Current.TrackerBlockerEnabled)
                    {
                        ShowTrackerToast(GetTrackerCountHint(tab.Url));
                    }
                    QueueGoogleAuthCheck(tab.Url, forceToast: true);
                }
            }, "browser-loading-state");
        };

        browser.LoadError += (_, args) =>
        {
            var failingUrl = args.FailedUrl ?? tab.Url;
            var errorCode = args.ErrorCode;
            var errorText = args.ErrorText;
            RunOnUi(() =>
            {
                if (!ReferenceEquals(tab, _activeTab) || errorCode == CefErrorCode.Aborted)
                {
                    return;
                }

                RecordDiagnostic(new BrowserDiagnosticEvent("load-error", failingUrl, $"{errorCode}: {errorText}", Failed: true));
                StatusText.Text = $"Ladefehler: {errorCode}";
                ShowSlowLoadBar(failingUrl, $"Diese Seite konnte nicht vollstaendig geladen werden: {errorCode}. Du kannst neu laden oder ohne Schutz laden.");
                ShowBrowserErrorPage(tab, browser, "Seite konnte nicht geladen werden", failingUrl, $"{errorCode}: {errorText}");
                WriteBrowserDiagnostic("load-error", failingUrl, $"{errorCode}: {errorText}");
            }, "browser-load-error");
        };

        browser.FrameLoadEnd += async (_, args) =>
        {
            bool isMainFrame;
            string frameUrl;
            try
            {
                isMainFrame = args.Frame.IsMain;
                frameUrl = args.Url;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (isMainFrame && IsGoogleHost(frameUrl))
                {
                    await CheckGoogleBlockedPage(args.Frame);
                    QueueGoogleAuthCheck(frameUrl, forceToast: true);
                }

                if (isMainFrame)
                {
                    await ApplyPageFeatureScriptsAsync(args.Frame, frameUrl);
                }

                if (isMainFrame && NovaVideoCompatibilityRequestHandler.IsLikelyVideoPage(frameUrl))
                {
                    await InstallMediaErrorMonitorAsync(args.Frame, frameUrl);
                    await CheckVideoCodecSupport(args.Frame, frameUrl);
                }

                if (isMainFrame && NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(frameUrl))
                {
                    _ = TryApplyWebmFallbackAsync(browser, tab);
                }

                if (isMainFrame && NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(frameUrl))
                {
                    await CheckTargetVideoDomainState(args.Frame, frameUrl);
                }
            }
            catch (ObjectDisposedException)
            {
                // Frame/Browser wurde waehrend der Nachbearbeitung verworfen (Navigation, Tab-Wechsel, Schliessen). Kein Absturzgrund.
            }
        };

        tab.Browser = browser;
        return browser;
    }

    private static bool IsMediaConsoleMessage(string message)
    {
        return message.Contains("decode", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("[NovaMedia]", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("media", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("video", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("h264", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("aac", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("mse", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ApplyPageFeatureScriptsAsync(IFrame frame, string frameUrl)
    {
        if (!AddressParser.IsWebUrl(frameUrl))
        {
            return;
        }

        try
        {
        if (_settingsService.Current.DarkModeEnforcerEnabled)
        {
            const string darkModeScript = """
                (() => {
                  if (document.getElementById("nova-dark-mode-enforcer")) return;
                  const style = document.createElement("style");
                  style.id = "nova-dark-mode-enforcer";
                  style.textContent = `
                    html { color-scheme: dark !important; background: #08060d !important; }
                    body { background: #08060d !important; color: #f4ecff !important; }
                    input, textarea, select, button { color-scheme: dark !important; }
                    img, video, canvas, iframe, svg { filter: none !important; }
                  `;
                  document.documentElement.appendChild(style);
                })();
                """;

            await frame.EvaluateScriptAsync(darkModeScript);
        }

        if (_settingsService.Current.LazyMediaLoadingEnabled)
        {
            const string lazyMediaScript = """
                (() => {
                  const apply = root => {
                    root.querySelectorAll?.("img, iframe").forEach(el => {
                      if (!el.hasAttribute("loading")) el.setAttribute("loading", "lazy");
                      if (el.tagName === "IMG") el.decoding = "async";
                    });
                  };

                  apply(document);

                  if (window.__novaLazyMediaObserver) return;
                  window.__novaLazyMediaObserver = new MutationObserver(records => {
                    for (const record of records) {
                      for (const node of record.addedNodes) {
                        if (node.nodeType === 1) apply(node);
                      }
                    }
                  });
                  window.__novaLazyMediaObserver.observe(document.documentElement, {
                    childList: true,
                    subtree: true
                  });
                })();
                """;

            await frame.EvaluateScriptAsync(lazyMediaScript);
        }
        }
        catch (ObjectDisposedException)
        {
            // Frame wurde beim Wegnavigieren/Tab-Wechsel verworfen, bevor die Skripte liefen. Harmlos.
        }
    }

    // Baut das Menue je nach Kontext auf: Link- und Bild-Bereich getrennt durch
    // Trennlinien (wie in Chrome, wenn ein Bild in einem Link liegt). Ohne Treffer
    // erscheint das Seiten-Menue. "Untersuchen" steht immer unten.
    private void ShowWebContextMenu(string linkUrl, string imageUrl, string pageUrl)
    {
        WebContextMenuItems.Children.Clear();

        var hasLink = !string.IsNullOrEmpty(linkUrl);
        var hasImage = !string.IsNullOrEmpty(imageUrl);

        if (hasLink)
        {
            AddWebContextItem("Link in neuem Tab öffnen", () => CreateTab(linkUrl, select: true));
            AddWebContextItem("Link in neuem Fenster öffnen", () => CreateTab(linkUrl, select: true));
            AddWebContextItem("Link in Inkognito-Fenster öffnen", () => CreateTab(linkUrl, select: true));
            AddWebContextSeparator();
            AddWebContextItem("Link speichern unter…", () => _activeTab?.Browser?.GetBrowserHost()?.StartDownload(linkUrl));
            AddWebContextItem("Adresse des Links kopieren", () => CopyToClipboardSafe(linkUrl));
        }

        if (hasImage)
        {
            if (hasLink)
            {
                AddWebContextSeparator();
            }

            AddWebContextItem("Bild in neuem Tab öffnen", () => CreateTab(imageUrl, select: true));
            AddWebContextItem("Bild speichern unter…", () => _activeTab?.Browser?.GetBrowserHost()?.StartDownload(imageUrl));
            AddWebContextItem("Bildadresse kopieren", () => CopyToClipboardSafe(imageUrl));
        }

        if (!hasLink && !hasImage)
        {
            AddWebContextItem("Seite speichern unter…", () =>
                _activeTab?.Browser?.GetBrowserHost()?.StartDownload(string.IsNullOrEmpty(pageUrl) ? (_activeTab?.Url ?? "") : pageUrl));
            AddWebContextItem("Neu laden", () => _activeTab?.Browser?.Reload());
            AddWebContextItem("Zu Favoriten hinzufügen", () => AddCurrentPageToBookmarks(pageUrl));
            AddWebContextSeparator();
            AddWebContextItem("Hintergrund anpassen", () => NavigateActive("nova://settings/design"));
        }

        AddWebContextSeparator();
        AddWebContextItem("Untersuchen", () => _activeTab?.Browser?.GetBrowserHost()?.ShowDevTools());

        WebContextMenu.IsOpen = false;
        WebContextMenu.IsOpen = true;
    }

    private void AddWebContextItem(string label, Action action)
    {
        var btn = new Button
        {
            Content = label,
            Style = (Style)FindResource("WebContextItem")
        };
        btn.Click += (_, _) =>
        {
            WebContextMenu.IsOpen = false;
            try { action(); }
            catch (Exception ex) { StatusText.Text = $"Aktion fehlgeschlagen: {ex.Message}"; }
        };
        WebContextMenuItems.Children.Add(btn);
    }

    private void AddWebContextSeparator()
    {
        WebContextMenuItems.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(6, 5, 6, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x31))
        });
    }

    private void CopyToClipboardSafe(string text)
    {
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = "In Zwischenablage kopiert.";
        }
        catch
        {
            StatusText.Text = "Kopieren fehlgeschlagen.";
        }
    }

    private void AddCurrentPageToBookmarks(string pageUrl)
    {
        var url = string.IsNullOrEmpty(pageUrl) ? (_activeTab?.Url ?? "") : pageUrl;
        if (!AddressParser.IsWebUrl(url))
        {
            StatusText.Text = "Nur echte Webseiten koennen als Favorit gespeichert werden.";
            return;
        }

        _bookmarkService.SaveOrUpdate(string.IsNullOrWhiteSpace(_activeTab?.Title) ? url : _activeTab!.Title, url, "");
        StatusText.Text = "Zu Favoriten hinzugefuegt.";
    }

    private static bool IsFatalMediaConsoleMessage(string message)
    {
        return message.Contains("[NovaMedia]", StringComparison.OrdinalIgnoreCase) &&
               (message.Contains("\"event\":\"error\"", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("MEDIA_ERR_SRC_NOT_SUPPORTED", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("MEDIA_ERR_DECODE", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("decode", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildMediaErrorMessage(string message)
    {
        var codecHint = message.Contains("MEDIA_ERR_SRC_NOT_SUPPORTED", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("MEDIA_ERR_DECODE", StringComparison.OrdinalIgnoreCase) ||
                        message.Contains("decode", StringComparison.OrdinalIgnoreCase)
            ? " Wahrscheinlich fehlt H.264/AAC/MP4 in der aktuellen CEF-Runtime."
            : "";
        return "Media-Fehler erkannt: Dieses Video/Audio kann nicht sauber abgespielt werden." + codecHint + " Oeffne Media fuer den Codec-Test oder DevTools fuer Details.";
    }

    private async Task InstallMediaErrorMonitorAsync(IFrame frame, string frameUrl)
    {
        const string script = """
            (() => {
                if (window.__novaMediaMonitorInstalled) {
                    return 'already-installed';
                }
                window.__novaMediaMonitorInstalled = true;

                const codeName = (code) => ({
                    1: 'MEDIA_ERR_ABORTED',
                    2: 'MEDIA_ERR_NETWORK',
                    3: 'MEDIA_ERR_DECODE',
                    4: 'MEDIA_ERR_SRC_NOT_SUPPORTED'
                })[code] || 'MEDIA_ERR_UNKNOWN';

                const mediaSource = (element) => {
                    const direct = element.currentSrc || element.src || '';
                    if (direct) return direct;
                    const source = element.querySelector('source[src]');
                    return source ? source.src : '';
                };

                const report = (eventName, element) => {
                    const error = element.error;
                    const payload = {
                        event: eventName,
                        tag: element.tagName.toLowerCase(),
                        code: error ? error.code : 0,
                        codeName: error ? codeName(error.code) : 'none',
                        message: error && error.message ? error.message : '',
                        networkState: element.networkState,
                        readyState: element.readyState,
                        currentTime: Number.isFinite(element.currentTime) ? Math.round(element.currentTime * 10) / 10 : 0,
                        src: mediaSource(element).slice(0, 420)
                    };
                    console.warn('[NovaMedia]' + JSON.stringify(payload));
                };

                const attach = (element) => {
                    if (!element || element.__novaMediaWatched) return;
                    element.__novaMediaWatched = true;
                    element.addEventListener('error', () => report('error', element), true);
                    element.addEventListener('stalled', () => report('stalled', element), true);
                    element.addEventListener('waiting', () => report('waiting', element), true);
                    element.addEventListener('abort', () => report('abort', element), true);
                    if (element.error) {
                        report('error', element);
                    }
                };

                const scan = () => document.querySelectorAll('video,audio').forEach(attach);
                scan();
                new MutationObserver(scan).observe(document.documentElement, { childList: true, subtree: true });
                return 'installed|' + document.querySelectorAll('video,audio').length;
            })();
            """;

        try
        {
            var result = await frame.EvaluateScriptAsync(script);
            if (result.Success && result.Result is string status)
            {
                RecordDiagnostic(new BrowserDiagnosticEvent("media-monitor", frameUrl, status, Media: true));
            }
        }
        catch
        {
            // Der Monitor ist nur Diagnose und darf die Seite nicht stoeren.
        }
    }

    private async Task CheckVideoCodecSupport(IFrame frame, string frameUrl)
    {
        const string script = """
            (() => {
                const video = document.createElement('video');
                return [
                    video.canPlayType('video/mp4; codecs="avc1.42E01E, mp4a.40.2"') || 'no',
                    video.canPlayType('video/webm; codecs="vp9, opus"') || 'no',
                    document.querySelectorAll('video').length
                ].join('|');
            })();
            """;

        JavascriptResponse result;
        try
        {
            result = await frame.EvaluateScriptAsync(script);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (!result.Success || result.Result is not string summary)
        {
            return;
        }

        var parts = summary.Split('|');
        var mp4 = parts.ElementAtOrDefault(0) ?? "no";
        var webm = parts.ElementAtOrDefault(1) ?? "no";
        var videoCount = parts.ElementAtOrDefault(2) ?? "0";
        _diagnostics.CodecSummary = $"MP4/H.264/AAC={mp4}, WebM/VP9={webm}, Video-Elemente={videoCount}";

        await Dispatcher.InvokeAsync(() =>
        {
            if (mp4.Equals("no", StringComparison.OrdinalIgnoreCase) && !webm.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = $"Video-Test: MP4/H.264 fehlt, WebM ist verfuegbar. Video-Elemente: {videoCount}.";
                RecordDiagnostic(new BrowserDiagnosticEvent(
                    "unsupported-mp4-codec",
                    frameUrl,
                    "MP4/H.264/AAC fehlt in dieser CEFSharp-Version. Nova stoppt die Seite nicht mehr, damit der Player WebM-Fallbacks laden kann.",
                    Failed: true,
                    Media: true));
            }
            else
            {
                StatusText.Text = $"Video-Test: MP4={mp4}, WebM={webm}, Video-Elemente={videoCount}.";
            }
        });
        WriteVideoDiagnostic(frameUrl, mp4, webm, videoCount);
    }

    private async Task CheckTargetVideoDomainState(IFrame frame, string frameUrl)
    {
        const string script = """
            (() => {
                let localStorageOk = false;
                let sessionStorageOk = false;
                try { localStorage.setItem('__nova_probe', '1'); localStorage.removeItem('__nova_probe'); localStorageOk = true; } catch {}
                try { sessionStorage.setItem('__nova_probe', '1'); sessionStorage.removeItem('__nova_probe'); sessionStorageOk = true; } catch {}
                const cookieCount = document.cookie ? document.cookie.split(';').filter(Boolean).length : 0;
                const text = document.body ? document.body.innerText.toLowerCase() : '';
                const consentHint = text.includes('cookie') || text.includes('consent') || text.includes('zustimmen') || text.includes('akzeptieren') || text.includes('age') || text.includes('alter');
                const sourceErrorHint = text.includes('quellenfehler') || text.includes('source error') || text.includes('no playable sources') || text.includes('media_err_src_not_supported');
                const videoCount = document.querySelectorAll('video').length;
                return JSON.stringify({ cookieCount, localStorageOk, sessionStorageOk, consentHint, sourceErrorHint, videoCount });
            })();
            """;

        try
        {
            var result = await frame.EvaluateScriptAsync(script);
            if (result.Success && result.Result is string summary)
            {
                RecordDiagnostic(new BrowserDiagnosticEvent("target-domain-state", frameUrl, summary));
                if (summary.Contains("\"sourceErrorHint\":true", StringComparison.OrdinalIgnoreCase))
                {
                    RecordDiagnostic(new BrowserDiagnosticEvent("media-source-error", frameUrl, "Player meldet Quellenfehler/source error.", Failed: true, Media: true));
                }
                WriteBrowserDiagnostic("target-domain-state", frameUrl, summary);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task CheckTargetVideoDomainStateFromBrowserAsync(ChromiumWebBrowser browser, BrowserTab tab)
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (!ReferenceEquals(tab, _activeTab) || !NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(tab.Url))
        {
            return;
        }

        const string script = """
            (() => {
                let localStorageOk = false;
                let sessionStorageOk = false;
                try { localStorage.setItem('__nova_probe', '1'); localStorage.removeItem('__nova_probe'); localStorageOk = true; } catch {}
                try { sessionStorage.setItem('__nova_probe', '1'); sessionStorage.removeItem('__nova_probe'); sessionStorageOk = true; } catch {}
                const cookieCount = document.cookie ? document.cookie.split(';').filter(Boolean).length : 0;
                const text = document.body ? document.body.innerText.toLowerCase() : '';
                const consentHint = text.includes('cookie') || text.includes('consent') || text.includes('zustimmen') || text.includes('akzeptieren') || text.includes('age') || text.includes('alter');
                const sourceErrorHint = text.includes('quellenfehler') || text.includes('source error') || text.includes('no playable sources') || text.includes('media_err_src_not_supported');
                const videoCount = document.querySelectorAll('video').length;
                return JSON.stringify({ cookieCount, localStorageOk, sessionStorageOk, consentHint, sourceErrorHint, videoCount });
            })();
            """;

        try
        {
            var result = await browser.EvaluateScriptAsync(script);
            if (result.Success && result.Result is string summary)
            {
                RecordDiagnostic(new BrowserDiagnosticEvent("target-domain-state", tab.Url, summary));
                if (summary.Contains("\"sourceErrorHint\":true", StringComparison.OrdinalIgnoreCase))
                {
                    RecordDiagnostic(new BrowserDiagnosticEvent("media-source-error", tab.Url, "Player meldet Quellenfehler/source error.", Failed: true, Media: true));
                    ShowSlowLoadBar(tab.Url, "Der Player meldet Quellenfehler. Oeffne die Media-Diagnose: Wenn MP4/H.264/AAC fehlt, braucht Nova einen CEF-Build mit proprietary_codecs.");
                }
                WriteBrowserDiagnostic("target-domain-state", tab.Url, summary);
            }
        }
        catch
        {
            // Diagnose darf die Seite nicht beeinflussen.
        }
    }

    private async Task TryApplyWebmFallbackAsync(ChromiumWebBrowser browser, BrowserTab tab)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (!ReferenceEquals(tab, _activeTab) || !NovaVideoCompatibilityRequestHandler.NeedsCompatibilityMode(tab.Url))
        {
            return;
        }

        const string script = """
            (() => {
                const video = document.querySelector('video');
                if (!video || (!video.paused && video.currentTime > 1)) {
                    return 'playing-or-no-video';
                }

                const candidates = new Set();
                document.querySelectorAll('source[src*=".webm"], video[src*=".webm"], [data-mediabook*=".webm"], [data-video*=".webm"]').forEach((node) => {
                    for (const attr of ['src', 'data-mediabook', 'data-video']) {
                        const value = node.getAttribute(attr);
                        if (value && value.includes('.webm')) candidates.add(value);
                    }
                });

                const html = document.documentElement.innerHTML;
                const matches = html.match(/https?:\\?\/\\?\/[^"'<>\\s]+?\.webm[^"'<>\\s]*/g) || [];
                matches.forEach((value) => candidates.add(value.replaceAll('\\/', '/')));

                const playable = [...candidates]
                    .map((value) => value.replace(/&amp;/g, '&'))
                    .filter((value) => value.startsWith('http'))
                    .filter((value) => !value.includes('/pics/gifs/'))
                    .sort((a, b) => {
                        const score = (url) => (url.includes('480P') ? 4 : 0) + (url.includes('360P') ? 3 : 0) + (url.includes('240P') ? 2 : 0) + (url.includes('180P') ? 1 : 0);
                        return score(b) - score(a);
                    });

                if (playable.length === 0) {
                    return 'no-webm-candidate';
                }

                video.pause();
                video.src = playable[0];
                video.preload = 'auto';
                video.load();
                video.play().catch(() => {});
                return 'webm-fallback|' + playable[0];
            })();
            """;

        try
        {
            var result = await browser.EvaluateScriptAsync(script);
            if (result.Success && result.Result is string status)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (status.StartsWith("webm-fallback|", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusText.Text = "Video-Kompatibilitaetsmodus: WebM-Fallback wurde aktiviert.";
                    }
                    else if (status.Equals("no-webm-candidate", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusText.Text = "Video-Kompatibilitaetsmodus: Keine direkte WebM-Quelle gefunden. MP4/H.264 fehlt in diesem CEF-Build.";
                    }
                });
            }
        }
        catch
        {
            // Der Fallback ist optional und darf die Seite nie unterbrechen.
        }
    }

    private static void WriteVideoDiagnostic(string url, string mp4, string webm, string videoCount)
    {
        try
        {
            var diagnosticsDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NovaBrowser.CefSharp");
            System.IO.Directory.CreateDirectory(diagnosticsDir);
            var line = $"{DateTimeOffset.Now:u}\tMP4={mp4}\tWebM={webm}\tVideos={videoCount}\tUrl={url}{Environment.NewLine}";
            System.IO.File.AppendAllText(System.IO.Path.Combine(diagnosticsDir, "media-diagnostics.log"), line);
        }
        catch
        {
            // Diagnose darf nie das Laden der Webseite stoeren.
        }
    }

    private void ShowSlowLoadBar(string url, string? message = null)
    {
        SlowLoadText.Text = message ?? "Diese Seite laedt ungewoehnlich lange. Pruefe Cookies, Tracker-Blocker oder Popups.";
        SlowLoadBar.Visibility = Visibility.Visible;
        StatusText.Text = "Seite laedt ungewoehnlich lange.";
        UpdateExtensionAccessState();
    }

    private void StopStuckLoad(ChromiumWebBrowser? browser, BrowserTab? tab, string reason, string message)
    {
        if (browser is null || tab is null || !ReferenceEquals(tab, _activeTab))
        {
            return;
        }

        try
        {
            if (browser.IsLoading)
            {
                browser.Stop();
            }
        }
        catch
        {
            // Stop() ist nur ein Komfort-Fix gegen haengende Drittanbieter-Requests.
        }

        _diagnostics.Stop();
        RecordDiagnostic(new BrowserDiagnosticEvent(reason, tab.Url, message, Failed: reason.Contains("codec", StringComparison.OrdinalIgnoreCase)));
        ShowSlowLoadBar(tab.Url, message);
        StatusText.Text = "Laden gestoppt, Seite bleibt bedienbar.";
        WriteBrowserDiagnostic(reason, tab.Url, message);
    }

    private void ReloadSlowPage_Click(object sender, RoutedEventArgs e)
    {
        SlowLoadBar.Visibility = Visibility.Collapsed;
        _activeTab?.Browser?.Reload();
    }

    private void DisableProtectionForPage_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null || !TryGetDomain(_activeTab.Url, out var domain))
        {
            StatusText.Text = "Fuer diese Seite kann kein Schutz deaktiviert werden.";
            return;
        }

        EnsureProtectionDisabledForDomain(_activeTab.Url);
        SlowLoadBar.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Schutz fuer {domain} deaktiviert. Seite wird neu geladen.";
        _activeTab.Browser?.Reload();
    }

    private void EnsureProtectionDisabledForDomain(string url)
    {
        if (!TryGetDomain(url, out var domain))
        {
            return;
        }

        if (!_addonService.Settings.TryGetValue(ProtectionDisabledDomainsSettingsKey, out var domains))
        {
            domains = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _addonService.Settings[ProtectionDisabledDomainsSettingsKey] = domains;
        }

        if (!domains.ContainsKey(domain))
        {
            domains[domain] = "disabled";
            _addonService.Save();
            RecordDiagnostic(new BrowserDiagnosticEvent("protection-disabled", url, $"All Nova protection disabled for {domain}."));
        }

    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        NavigateActive("nova://diagnostics");
    }

    private void OpenMediaDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        NavigateActive("nova://media-diagnostics");
    }

    private void RunMediaDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        _ = RunMediaDiagnosticsAsync();
    }

    private void OpenDevTools_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.Browser is null || AddressParser.IsInternalUrl(_activeTab.Url))
        {
            StatusText.Text = "DevTools sind fuer echte Webseiten verfuegbar.";
            return;
        }

        _activeTab.Browser.ShowDevTools();
        StatusText.Text = "DevTools geoeffnet.";
    }

    private void ClearCookiesForPage_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null || !TryGetDomain(_activeTab.Url, out var domain))
        {
            StatusText.Text = "Fuer diese Seite konnten keine Cookies geloescht werden.";
            return;
        }

        var cookieManager = Cef.GetGlobalCookieManager();
        cookieManager?.DeleteCookies("https://" + domain, "");
        cookieManager?.DeleteCookies("http://" + domain, "");
        if (domain.Contains("pornhub", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var relatedDomain in new[] { "phncdn.com", "pornhubpremium.com", "trafficjunky.net", "jwpcdn.com", "jwpin.com" })
            {
                cookieManager?.DeleteCookies("https://" + relatedDomain, "");
                cookieManager?.DeleteCookies("http://" + relatedDomain, "");
            }
        }

        RecordDiagnostic(new BrowserDiagnosticEvent("cookies-cleared", _activeTab.Url, $"Cookies cleared for {domain}."));
        StatusText.Text = $"Cookies fuer {domain} geloescht. Seite wird neu geladen.";
        SlowLoadBar.Visibility = Visibility.Collapsed;
        _activeTab.Browser?.Reload();
    }

    private void RecordDiagnostic(BrowserDiagnosticEvent diagnostic)
    {
        Dispatcher.Invoke(() =>
        {
            _diagnostics.Record(diagnostic);
            RefreshDiagnosticsPage();
        });
    }

    private void RefreshDiagnosticsPage()
    {
        DiagCurrentUrlText.Text = _activeTab?.Url ?? "-";
        DiagCodecText.Text = _diagnostics.CodecSummary;
        DiagRequestText.Text = $"{_diagnostics.RequestCount} Requests, {_diagnostics.MediaRequestCount} Media, {_diagnostics.FailedRequestCount} fehlgeschlagen";
        DiagBlockPopupText.Text = $"{_diagnostics.BlockedRequestCount} blockiert, {_diagnostics.PopupCount} Popups/target=_blank";
        DiagEventsText.Text = _diagnostics.RecentEvents.Count == 0
            ? "Noch keine Diagnoseereignisse."
            : string.Join(Environment.NewLine, _diagnostics.RecentEvents);
        DiagFailedRequestsText.Text = FormatDiagnosticList(_diagnostics.FailedRequests);
        DiagBlockedRequestsText.Text = FormatDiagnosticList(_diagnostics.BlockedRequests);
        DiagConsoleErrorsText.Text = FormatDiagnosticList(_diagnostics.ConsoleErrors);
        DiagRedirectsText.Text = FormatDiagnosticList(_diagnostics.Redirects);
        DiagMediaErrorsText.Text = FormatDiagnosticList(_diagnostics.MediaErrors);
    }

    private static string FormatDiagnosticList(IReadOnlyCollection<string> entries)
    {
        return entries.Count == 0
            ? "Noch keine Eintraege."
            : string.Join(Environment.NewLine, entries);
    }

    private async Task RunMediaDiagnosticsAsync()
    {
        MediaVersionText.Text =
            $"CEFSharp: {GetCefStaticValue("CefSharpVersion")}\n" +
            $"CEF: {GetCefStaticValue("CefVersion")}\n" +
            $"Chromium: {GetCefStaticValue("ChromiumVersion")}";
        MediaHardwareText.Text = "WebGL: wird geprueft...";
        MediaSummaryText.Text = "Media-Test laeuft...";
        MediaCodecRowsText.Text = "Wird geprueft...";
        MediaVerdictText.Text = "Wird bewertet...";

        var browser = await GetMediaProbeBrowserAsync();
        if (browser is null)
        {
            MediaSummaryText.Text = "Der interne CEF-Testbrowser konnte nicht gestartet werden.";
            MediaCodecRowsText.Text = "Kein Ergebnis.";
            MediaVerdictText.Text = "Bitte NovaBrowser neu starten und die Diagnose erneut oeffnen.";
            WriteBrowserDiagnostic("media-diagnostics-error", _activeTab?.Url ?? "nova://media-diagnostics", "Internal CEF probe did not initialize.");
            return;
        }

        const string script = """
            (() => {
                const video = document.createElement('video');
                const audio = document.createElement('audio');
                let webgl = false;
                try {
                    const canvas = document.createElement('canvas');
                    const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
                    webgl = !!gl;
                } catch {}
                const safe = (value) => value || 'no';
                return [
                    `userAgent=${navigator.userAgent}`,
                    `mp4=${safe(video.canPlayType('video/mp4'))}`,
                    `h264=${safe(video.canPlayType('video/mp4; codecs="avc1.42E01E"'))}`,
                    `aac=${safe(audio.canPlayType('audio/mp4; codecs="mp4a.40.2"') || video.canPlayType('audio/mp4; codecs="mp4a.40.2"'))}`,
                    `webm=${safe(video.canPlayType('video/webm'))}`,
                    `mediaSource=${!!window.MediaSource}`,
                    `webgl=${webgl}`
                ].join('\n');
            })();
            """;

        try
        {
            var result = await browser.EvaluateScriptAsync(script);
            if (!result.Success || result.Result is not string raw)
            {
                MediaSummaryText.Text = "Media-Test konnte nicht ausgefuehrt werden.";
                MediaCodecRowsText.Text = result.Message ?? "Kein JavaScript-Ergebnis.";
                MediaVerdictText.Text = "Keine Bewertung moeglich.";
                WriteBrowserDiagnostic("media-diagnostics-error", _activeTab?.Url ?? "nova://media-diagnostics", result.Message ?? "No JavaScript result.");
                return;
            }

            ApplyMediaDiagnosticResult(raw);
        }
        catch (Exception ex)
        {
            MediaSummaryText.Text = "Media-Test ist fehlgeschlagen.";
            MediaCodecRowsText.Text = ex.Message;
            MediaVerdictText.Text = "Keine Bewertung moeglich.";
            WriteBrowserDiagnostic("media-diagnostics-error", _activeTab?.Url ?? "nova://media-diagnostics", ex.Message);
        }
    }

    private async Task<ChromiumWebBrowser?> GetMediaProbeBrowserAsync()
    {
        if (_activeTab?.Browser is { IsBrowserInitialized: true, CanExecuteJavascriptInMainFrame: true } activeBrowser)
        {
            return activeBrowser;
        }

        if (_mediaProbeBrowser is null)
        {
            _mediaProbeBrowser = new ChromiumWebBrowser(DarkBlankPage)
            {
                BrowserSettings = new CefSharp.BrowserSettings
                {
                    Javascript = CefState.Enabled,
                    LocalStorage = CefState.Enabled,
                    WebGl = CefState.Enabled,
                    WindowlessFrameRate = 60,
                    BackgroundColor = Cef.ColorSetARGB(255, 11, 9, 17)
                }
            };
        }

        BrowserHost.Content = _mediaProbeBrowser;
        if (!(_mediaProbeBrowser.Address ?? "").StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
        {
            _mediaProbeBrowser.Load("data:text/html,%3Chtml%3E%3Cbody%3ENova%20Media%20Probe%3C/body%3E%3C/html%3E");
        }

        for (var i = 0; i < 60; i++)
        {
            if (_mediaProbeBrowser.IsBrowserInitialized && _mediaProbeBrowser.CanExecuteJavascriptInMainFrame)
            {
                return _mediaProbeBrowser;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private void ApplyMediaDiagnosticResult(string raw)
    {
        var values = raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        string Get(string key) => values.TryGetValue(key, out var value) ? value : "unbekannt";
        static bool Missing(string value) => string.IsNullOrWhiteSpace(value) ||
                                             value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                                             value.Equals("false", StringComparison.OrdinalIgnoreCase);

        var h264 = Get("h264");
        var aac = Get("aac");
        var mp4 = Get("mp4");
        var missingProprietary = Missing(h264) || Missing(aac) || Missing(mp4);

        MediaStatusCard.Background = missingProprietary
            ? new SolidColorBrush(Color.FromRgb(64, 22, 32))
            : new SolidColorBrush(Color.FromRgb(18, 64, 42));
        MediaStatusCard.BorderBrush = missingProprietary
            ? new SolidColorBrush(Color.FromRgb(255, 90, 115))
            : new SolidColorBrush(Color.FromRgb(62, 220, 142));
        MediaStatusText.Text = missingProprietary
            ? "Video-Codecs fehlen. Einige Webseiten-Videos koennen nicht abgespielt werden."
            : "Media-Support sieht gut aus.";

        MediaVersionText.Text =
            $"CEFSharp: {GetCefStaticValue("CefSharpVersion")}\n" +
            $"CEF: {GetCefStaticValue("CefVersion")}\n" +
            $"Chromium: {GetCefStaticValue("ChromiumVersion")}\n" +
            $"User-Agent: {Get("userAgent")}";
        MediaHardwareText.Text = $"WebGL verfuegbar: {Get("webgl")}";
        MediaSummaryText.Text =
            $"MediaSource verfuegbar: {Get("mediaSource")}\n" +
            $"WebGL verfuegbar: {Get("webgl")}";

        MediaCodecRowsText.Text =
            $"canPlayType(\"video/mp4\")                         : {mp4}\n" +
            $"canPlayType('video/mp4; codecs=\"avc1.42E01E\"') : {h264}\n" +
            $"canPlayType('audio/mp4; codecs=\"mp4a.40.2\"')   : {aac}\n" +
            $"canPlayType(\"video/webm\")                        : {Get("webm")}";
        SetMediaTableRow(MediaTableMp4Result, MediaTableMp4Rating, mp4);
        SetMediaTableRow(MediaTableH264Result, MediaTableH264Rating, h264);
        SetMediaTableRow(MediaTableAacResult, MediaTableAacRating, aac);
        SetMediaTableRow(MediaTableWebmResult, MediaTableWebmRating, Get("webm"));
        SetMediaTableRow(MediaTableMseResult, MediaTableMseRating, Get("mediaSource"));
        SetMediaTableRow(MediaTableWebglResult, MediaTableWebglRating, Get("webgl"));

        MediaVerdictText.Text = missingProprietary
            ? "Die aktuelle CEFSharp-Version unterstuetzt die benoetigten proprietaeren Codecs nicht. Fuer H.264/AAC/MP4 wird ein Custom-CEF-Build mit proprietary_codecs=true und ffmpeg_branding=Chrome benoetigt."
            : "Die wichtigsten MP4/H.264/AAC-Codecs sind verfuegbar. Wenn ein Video trotzdem haengt, liegt es eher an Player-Logik, DRM/EME oder einem CDN-Fehler.";

        _diagnostics.CodecSummary = $"MP4={mp4}, H264={h264}, AAC={aac}, WebM={Get("webm")}, MediaSource={Get("mediaSource")}, WebGL={Get("webgl")}";
        RefreshDiagnosticsPage();
        WriteBrowserDiagnostic(
            "media-diagnostics",
            _activeTab?.Url ?? "nova://media-diagnostics",
            $"{MediaVersionText.Text.Replace(Environment.NewLine, " | ")} | {MediaHardwareText.Text.Replace(Environment.NewLine, " | ")} | {MediaCodecRowsText.Text.Replace(Environment.NewLine, " | ")} | {MediaSummaryText.Text.Replace(Environment.NewLine, " | ")}");
    }

    private static void SetMediaTableRow(TextBlock resultText, TextBlock ratingText, string result)
    {
        resultText.Text = result;
        var ok = !string.IsNullOrWhiteSpace(result) &&
                 !result.Equals("no", StringComparison.OrdinalIgnoreCase) &&
                 !result.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                 !result.Equals("unbekannt", StringComparison.OrdinalIgnoreCase);
        ratingText.Text = ok ? "OK" : "Fehlt";
        ratingText.Foreground = ok
            ? new SolidColorBrush(Color.FromRgb(128, 255, 192))
            : new SolidColorBrush(Color.FromRgb(255, 138, 158));
    }

    private async void LoadTestVideo_Click(object sender, RoutedEventArgs e)
    {
        MediaVideoEventText.Text = "Testvideo wird geladen...";
        var browser = await GetMediaProbeBrowserAsync();
        if (browser is null)
        {
            MediaVideoEventText.Text = "Testvideo konnte nicht gestartet werden.";
            return;
        }

        const string script = """
            (() => {
                const old = document.getElementById('__nova_test_video');
                if (old) old.remove();
                window.__novaVideoEvents = [];
                const video = document.createElement('video');
                video.id = '__nova_test_video';
                video.controls = true;
                video.muted = true;
                video.width = 320;
                video.height = 180;
                video.style.cssText = 'position:fixed;left:12px;bottom:12px;z-index:2147483647;background:#000;border:1px solid #a968ff;border-radius:8px;';
                video.src = 'https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4';
                ['canplay', 'error', 'loadedmetadata', 'stalled', 'waiting'].forEach((name) => {
                    video.addEventListener(name, () => {
                        window.__novaVideoEvents.push(name + (video.error ? ': code ' + video.error.code : ''));
                    });
                });
                document.body.appendChild(video);
                video.load();
                return 'started';
            })();
            """;

        var start = await browser.EvaluateScriptAsync(script);
        if (!start.Success)
        {
            MediaVideoEventText.Text = start.Message ?? "Testvideo konnte nicht geladen werden.";
            return;
        }

        await Task.Delay(4500);
        var events = await browser.EvaluateScriptAsync("window.__novaVideoEvents ? window.__novaVideoEvents.join(', ') : ''");
        MediaVideoEventText.Text = events.Result?.ToString() is { Length: > 0 } value
            ? $"Events: {value}"
            : "Noch kein Event nach 4,5 Sekunden.";
    }

    private static string GetCefStaticValue(string propertyName)
    {
        return typeof(Cef).GetProperty(propertyName)?.GetValue(null)?.ToString() ?? "unbekannt";
    }

    private bool IsProtectionDisabledForUrl(string? url)
    {
        return TryGetDomain(url, out var domain) &&
               _addonService.Settings.TryGetValue(ProtectionDisabledDomainsSettingsKey, out var domains) &&
               domains.ContainsKey(domain);
    }

    private static bool TryGetDomain(string? url, out string domain)
    {
        domain = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        domain = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(domain);
    }

    private static void WriteBrowserDiagnostic(string kind, string url, string details)
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
            // Diagnose darf nie Navigation oder Rendering blockieren.
        }
    }

    private void RunOnUi(Action action, string source)
    {
        if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        void SafeRun()
        {
            if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                App.LogException(source, ex);
                StatusText.Text = "Nova hat einen internen Fehler abgefangen und laeuft weiter.";
            }
        }

        try
        {
            if (Dispatcher.CheckAccess())
            {
                SafeRun();
            }
            else
            {
                Dispatcher.BeginInvoke((Action)SafeRun);
            }
        }
        catch (Exception ex)
        {
            App.LogException(source, ex);
        }
    }

    private sealed class BrowserDiagnosticsState
    {
        private readonly Queue<string> _recentEvents = new();
        private readonly Queue<string> _failedRequests = new();
        private readonly Queue<string> _blockedRequests = new();
        private readonly Queue<string> _consoleErrors = new();
        private readonly Queue<string> _redirects = new();
        private readonly Queue<string> _mediaErrors = new();
        private DateTimeOffset? _startedAt;

        public string CurrentUrl { get; private set; } = "-";
        public int RequestCount { get; private set; }
        public int MediaRequestCount { get; private set; }
        public int FailedRequestCount { get; private set; }
        public int BlockedRequestCount { get; private set; }
        public int PopupCount { get; private set; }
        public string CodecSummary { get; set; } = "Noch nicht getestet.";
        public IReadOnlyCollection<string> RecentEvents => _recentEvents.ToArray();
        public IReadOnlyCollection<string> FailedRequests => _failedRequests.ToArray();
        public IReadOnlyCollection<string> BlockedRequests => _blockedRequests.ToArray();
        public IReadOnlyCollection<string> ConsoleErrors => _consoleErrors.ToArray();
        public IReadOnlyCollection<string> Redirects => _redirects.ToArray();
        public IReadOnlyCollection<string> MediaErrors => _mediaErrors.ToArray();

        public void Start(string url)
        {
            CurrentUrl = url;
            _startedAt = DateTimeOffset.Now;
            RequestCount = 0;
            MediaRequestCount = 0;
            FailedRequestCount = 0;
            BlockedRequestCount = 0;
            PopupCount = 0;
            _recentEvents.Clear();
            _failedRequests.Clear();
            _blockedRequests.Clear();
            _consoleErrors.Clear();
            _redirects.Clear();
            _mediaErrors.Clear();
            AddEvent("load-start", url, "Navigation gestartet.");
        }

        public void Stop()
        {
            if (_startedAt is { } start)
            {
                AddEvent("load-stop", CurrentUrl, $"Fertig nach {(DateTimeOffset.Now - start).TotalSeconds:0.0}s.");
            }
        }

        public void Record(BrowserDiagnosticEvent diagnostic)
        {
            if (diagnostic.Kind == "request")
            {
                RequestCount++;
            }

            if (diagnostic.Media)
            {
                MediaRequestCount++;
            }

            if (diagnostic.Failed)
            {
                FailedRequestCount++;
            }

            if (diagnostic.Blocked || diagnostic.Kind.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                BlockedRequestCount++;
            }

            if (diagnostic.Kind.Contains("popup", StringComparison.OrdinalIgnoreCase))
            {
                PopupCount++;
            }

            if (diagnostic.Kind != "request")
            {
                AddEvent(diagnostic.Kind, diagnostic.Url, diagnostic.Details);
            }

            if (diagnostic.Failed)
            {
                AddListEvent(_failedRequests, diagnostic);
            }

            if (diagnostic.Blocked || diagnostic.Kind.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            {
                AddListEvent(_blockedRequests, diagnostic);
            }

            if (diagnostic.Kind.Contains("console", StringComparison.OrdinalIgnoreCase))
            {
                AddListEvent(_consoleErrors, diagnostic);
            }

            if (diagnostic.Kind.Contains("redirect", StringComparison.OrdinalIgnoreCase))
            {
                AddListEvent(_redirects, diagnostic);
            }

            if ((diagnostic.Media && diagnostic.Failed) ||
                diagnostic.Kind.Contains("media-source-error", StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Kind.Contains("media-console", StringComparison.OrdinalIgnoreCase))
            {
                AddListEvent(_mediaErrors, diagnostic);
            }
        }

        private void AddEvent(string kind, string? url, string details)
        {
            var compactUrl = string.IsNullOrWhiteSpace(url) ? "-" : url;
            if (compactUrl.Length > 140)
            {
                compactUrl = compactUrl[..140] + "...";
            }

            _recentEvents.Enqueue($"{DateTime.Now:HH:mm:ss}  {kind}  {details}  {compactUrl}");
            while (_recentEvents.Count > 18)
            {
                _recentEvents.Dequeue();
            }
        }

        private static void AddListEvent(Queue<string> target, BrowserDiagnosticEvent diagnostic)
        {
            var compactUrl = string.IsNullOrWhiteSpace(diagnostic.Url) ? "-" : diagnostic.Url;
            if (compactUrl.Length > 110)
            {
                compactUrl = compactUrl[..110] + "...";
            }

            target.Enqueue($"{DateTime.Now:HH:mm:ss}  {diagnostic.Kind}  {diagnostic.Details}  {compactUrl}");
            while (target.Count > 30)
            {
                target.Dequeue();
            }
        }
    }

    private sealed class CookieNameVisitor : ICookieVisitor
    {
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource<HashSet<string>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HashSet<string>> Completion => _completion.Task;

        public bool Visit(CefSharp.Cookie cookie, int count, int total, ref bool deleteCookie)
        {
            _names.Add(cookie.Name);
            if (count >= total - 1)
            {
                _completion.TrySetResult(_names);
            }

            return true;
        }

        public void Dispose()
        {
            _completion.TrySetResult(_names);
        }
    }

    private void SelectTab(BrowserTab tab)
    {
        foreach (var existing in Tabs)
        {
            existing.IsSelected = ReferenceEquals(existing, tab);
        }

        _activeTab = tab;
        MarkTabActive(tab);
        WakeSnoozedTab(tab);
        CloseTransientPanels();

        if (IsStartTab(tab.Url))
        {
            BrowserHost.Content = null;
            _suppressOmniboxSuggestions = true;
            AddressBox.Text = AddressParser.HomeUrl;
            _suppressOmniboxSuggestions = false;
            ShowInternalPage(AddressParser.HomeUrl);
        }
        else if (AddressParser.IsInternalUrl(tab.Url))
        {
            BrowserHost.Content = null;
            _suppressOmniboxSuggestions = true;
            AddressBox.Text = tab.Url;
            _suppressOmniboxSuggestions = false;
            ShowInternalPage(tab.Url);
        }
        else
        {
            InternalHost.Visibility = Visibility.Collapsed;
            BrowserHost.Content = EnsureBrowser(tab);
            _suppressOmniboxSuggestions = true;
            AddressBox.Text = tab.Url;
            _suppressOmniboxSuggestions = false;
            if (tab.Browser?.Address != tab.Url)
            {
                tab.Browser?.Load(tab.Url);
            }
        }

        StatusText.Text = tab.Title;
        UpdateBookmarkButton();
        UpdateExtensionAccessState();
    }

    private void SelectRelativeTab(int delta)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var currentIndex = _activeTab is null ? 0 : Math.Max(0, Tabs.IndexOf(_activeTab));
        var nextIndex = (currentIndex + delta + Tabs.Count) % Tabs.Count;
        SelectTab(Tabs[nextIndex]);
    }

    private void SelectTabByShortcutIndex(int shortcutIndex)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var index = shortcutIndex == 9
            ? Tabs.Count - 1
            : Math.Clamp(shortcutIndex - 1, 0, Tabs.Count - 1);

        SelectTab(Tabs[index]);
    }

    private void FocusAddressBar()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void NavigateActive(string input)
    {
        var url = ApplyNavigationSettings(AddressParser.Normalize(input, _settingsService.Current.SearchEngine));
        CloseTransientPanels();
        CloseOmniboxSuggestions();
        if (_activeTab is null)
        {
            CreateTab(url, select: true);
            return;
        }

        GoogleNotice.Visibility = Visibility.Collapsed;
        _activeTab.Url = url;

        if (IsStartTab(url))
        {
            _activeTab.Title = "Neuer Tab";
            SelectTab(_activeTab);
            return;
        }

        if (AddressParser.IsInternalUrl(url))
        {
            _activeTab.Title = GetInternalTitle(url);
            SelectTab(_activeTab);
            return;
        }

        if (!AddressParser.IsWebUrl(url))
        {
            StatusText.Text = "Diese Adresse wird blockiert.";
            return;
        }

        var browser = EnsureBrowser(_activeTab);
        BrowserHost.Content = browser;
        InternalHost.Visibility = Visibility.Collapsed;
        _suppressOmniboxSuggestions = true;
        AddressBox.Text = url;
        _suppressOmniboxSuggestions = false;
        browser.Load(url);
    }

    private string ApplyNavigationSettings(string url)
    {
        if (!_settingsService.Current.HttpsOnlyModeEnabled ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        };

        return builder.Uri.ToString();
    }

    private void ShowCrashErrorPage(string? url, string details)
    {
        RunOnUi(() =>
        {
            if (_activeTab?.Browser is null)
            {
                return;
            }

            ShowBrowserErrorPage(
                _activeTab,
                _activeTab.Browser,
                "Diese Webseite ist abgestuerzt",
                string.IsNullOrWhiteSpace(url) ? _activeTab.Url : url,
                details);
        }, "show-crash-error-page");
    }

    private void ShowBrowserErrorPage(BrowserTab tab, ChromiumWebBrowser browser, string title, string failedUrl, string details)
    {
        if (!ReferenceEquals(tab, _activeTab) || string.IsNullOrWhiteSpace(failedUrl))
        {
            return;
        }

        tab.Url = failedUrl;
        tab.Title = "Seitenfehler";
        AddressBox.Text = failedUrl;
        SetLoadingAnimation(false);

        var html = BuildNovaErrorPage(title, failedUrl, details);
        browser.Load("data:text/html;charset=utf-8," + Uri.EscapeDataString(html));
    }

    private static string BuildNovaErrorPage(string title, string url, string details)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeUrl = WebUtility.HtmlEncode(url);
        var safeDetails = WebUtility.HtmlEncode(details);
        return $$"""
            <!doctype html>
            <html lang="de">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>{{safeTitle}}</title>
              <style>
                :root { color-scheme: dark; }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  min-height: 100vh;
                  display: grid;
                  place-items: center;
                  font-family: "Segoe UI", system-ui, sans-serif;
                  background:
                    radial-gradient(circle at 72% 18%, rgba(148, 59, 255, .30), transparent 38%),
                    radial-gradient(circle at 15% 80%, rgba(255, 47, 143, .18), transparent 34%),
                    #07050d;
                  color: #f6ecff;
                }
                .card {
                  width: min(760px, calc(100vw - 48px));
                  padding: 34px;
                  border-radius: 24px;
                  background: rgba(16, 10, 28, .84);
                  border: 1px solid rgba(193, 117, 255, .45);
                  box-shadow: 0 0 50px rgba(154, 72, 255, .22), inset 0 1px 0 rgba(255,255,255,.08);
                }
                .icon {
                  width: 58px;
                  height: 58px;
                  border-radius: 18px;
                  display: grid;
                  place-items: center;
                  margin-bottom: 18px;
                  background: linear-gradient(135deg, #ff2f8f, #8b3cff);
                  box-shadow: 0 0 28px rgba(187, 80, 255, .46);
                  font-size: 28px;
                  font-weight: 900;
                }
                h1 { margin: 0 0 10px; font-size: clamp(30px, 5vw, 52px); line-height: 1; letter-spacing: 0; }
                p { margin: 0 0 18px; color: #cdbce8; font-size: 16px; line-height: 1.6; }
                code {
                  display: block;
                  padding: 14px;
                  border-radius: 14px;
                  background: rgba(0,0,0,.35);
                  border: 1px solid rgba(173, 112, 255, .25);
                  color: #e7d8ff;
                  overflow-wrap: anywhere;
                }
                .details { margin-top: 12px; font-size: 13px; color: #a98fc7; }
                .actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 24px; }
                button {
                  border: 1px solid rgba(210, 140, 255, .42);
                  background: rgba(45, 20, 78, .85);
                  color: #fff;
                  border-radius: 999px;
                  padding: 12px 18px;
                  font-weight: 800;
                  cursor: pointer;
                }
                button.primary {
                  background: linear-gradient(135deg, #ff2f8f, #9b45ff);
                  box-shadow: 0 0 22px rgba(171, 80, 255, .35);
                }
                button:hover { filter: brightness(1.12); }
              </style>
            </head>
            <body>
              <main class="card">
                <div class="icon">!</div>
                <h1>{{safeTitle}}</h1>
                <p>NyxNova hat den Tab stabil gehalten und den Fehler abgefangen.</p>
                <code>{{safeUrl}}</code>
                <p class="details">{{safeDetails}}</p>
                <div class="actions">
                  <button class="primary" onclick="location.href='{{safeUrl}}'">Neu laden</button>
                  <button onclick="history.back()">Zurueck</button>
                  <button onclick="location.href='nova://start'">Startseite</button>
                  <button onclick="location.href='nova://diagnostics'">Diagnose oeffnen</button>
                </div>
              </main>
            </body>
            </html>
            """;
    }

    private void MarkTabActive(BrowserTab tab)
    {
        _tabLastActiveAt[tab] = DateTimeOffset.UtcNow;
    }

    private void WakeSnoozedTab(BrowserTab tab)
    {
        if (!_snoozedTabUrls.Remove(tab, out var url))
        {
            return;
        }

        tab.Url = url;
        if (tab.Browser is not null && !tab.Browser.IsDisposed)
        {
            tab.Browser.Load(url);
        }
    }

    private void EcoTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settingsService.Current.EcoModeEnabled || !_settingsService.Current.TabSleepEnabled)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);
        foreach (var tab in Tabs.ToList())
        {
            if (ReferenceEquals(tab, _activeTab) ||
                _snoozedTabUrls.ContainsKey(tab) ||
                !AddressParser.IsWebUrl(tab.Url) ||
                tab.Browser is null ||
                tab.Browser.IsDisposed ||
                tab.Browser.IsLoading)
            {
                continue;
            }

            var lastActive = _tabLastActiveAt.TryGetValue(tab, out var value)
                ? value
                : DateTimeOffset.UtcNow;

            if (lastActive > cutoff)
            {
                continue;
            }

            _snoozedTabUrls[tab] = tab.Url;
            tab.Browser.Load(DarkBlankPage);
            RecordDiagnostic(new BrowserDiagnosticEvent("eco-tab-snooze", tab.Url, "Inactive background tab unloaded by Eco Mode."));
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception ex)
        {
            App.LogException("rounded-corners", ex);
        }
    }

    public void ActivateFromSecondInstance(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            OpenInNewTab(url);
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OpenInNewTab(string url)
    {
        if (AddressParser.IsInternalUrl(url) || AddressParser.IsWebUrl(url))
        {
            var tab = CreateTab(url, select: true);
            if (AddressParser.IsWebUrl(tab.Url))
            {
                EnsureBrowser(tab).Load(tab.Url);
            }
            return;
        }

        StatusText.Text = "Unbekanntes Protokoll blockiert.";
    }

    private void HandleAuthCallback(string callbackUrl)
    {
        RunOnUi(() =>
        {
            var internalUrl = AddressParser.ToInternalAuthCallbackUrl(callbackUrl);
            RecordDiagnostic(new BrowserDiagnosticEvent("auth-callback-handled", callbackUrl, "Callback converted to internal Nova page."));

            if (_activeTab is null)
            {
                CreateTab(internalUrl, select: true);
                return;
            }

            _activeTab.Url = internalUrl;
            _activeTab.Title = "Google Callback";
            SelectTab(_activeTab);
            StatusText.Text = "Google Callback erhalten.";
        }, "auth-callback");
    }

    private void ShowAuthCallbackPage(string internalUrl)
    {
        var status = "Callback ohne Details empfangen.";
        try
        {
            var uri = new Uri(internalUrl);
            var query = ParseQuery(uri.Query);
            query.TryGetValue("code", out var code);
            query.TryGetValue("state", out var state);
            query.TryGetValue("error", out var error);
            query.TryGetValue("error_description", out var errorDescription);

            if (!string.IsNullOrWhiteSpace(error))
            {
                status = $"Google hat einen Fehler zurueckgegeben: {error}";
                if (!string.IsNullOrWhiteSpace(errorDescription))
                {
                    status += $"\n{Uri.UnescapeDataString(errorDescription)}";
                }
            }
            else if (!string.IsNullOrWhiteSpace(code))
            {
                status = "Google hat einen Autorisierungscode zurueckgegeben. Der Callback funktioniert jetzt in Nova.";
                if (!string.IsNullOrWhiteSpace(state))
                {
                    status += $"\nState: {state}";
                }
            }
            else
            {
                status = "Callback erhalten, aber kein code/error Parameter gefunden.";
            }
        }
        catch (Exception ex)
        {
            App.LogException("auth-callback-page", ex);
            status = "Callback wurde empfangen, konnte aber nicht vollstaendig gelesen werden.";
        }

        AuthCallbackStatusText.Text = status;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pieces[0].Replace("+", " "));
            var value = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1].Replace("+", " ")) : "";
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private void ShowInternalPage(string url)
    {
        InternalHost.Visibility = Visibility.Visible;
        StartPage.Visibility = Visibility.Collapsed;
        HistoryPage.Visibility = Visibility.Collapsed;
        DownloadsPage.Visibility = Visibility.Collapsed;
        AuthCallbackPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;
        MediaDiagnosticsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        UpdatePage.Visibility = Visibility.Collapsed;
        ExtensionsPage.Visibility = Visibility.Collapsed;
        ModsPage.Visibility = Visibility.Collapsed;
        ShadersPage.Visibility = Visibility.Collapsed;
        StorePage.Visibility = Visibility.Collapsed;
        AddonDetailPage.Visibility = Visibility.Collapsed;

        var route = StripInternalUrlSuffix(url).ToLowerInvariant();
        switch (route)
        {
            case "nova://start":
                StartPage.Visibility = Visibility.Visible;
                break;
            case "nova://history":
                HistoryPage.Visibility = Visibility.Visible;
                break;
            case "nova://downloads":
                DownloadsPage.Visibility = Visibility.Visible;
                break;
            case "nova://auth/callback":
                ShowAuthCallbackPage(url);
                AuthCallbackPage.Visibility = Visibility.Visible;
                break;
            case "nova://diagnostics":
                RefreshDiagnosticsPage();
                DiagnosticsPage.Visibility = Visibility.Visible;
                break;
            case "nova://media-diagnostics":
                MediaDiagnosticsPage.Visibility = Visibility.Visible;
                _ = RunMediaDiagnosticsAsync();
                break;
            case "nova://settings":
                SettingsPage.Visibility = Visibility.Visible;
                ShowSettingsCategory("design");
                break;
            case var settings when settings.StartsWith("nova://settings/", StringComparison.OrdinalIgnoreCase):
                SettingsPage.Visibility = Visibility.Visible;
                ShowSettingsCategory(settings["nova://settings/".Length..]);
                break;
            case "nova://update":
                UpdatePage.Visibility = Visibility.Visible;
                break;
            case "nova://extensions":
                ExtensionsPage.Visibility = Visibility.Visible;
                RefreshInstalledAddonList();
                break;
            case "nova://mods":
                ModsPage.Visibility = Visibility.Visible;
                break;
            case "nova://shaders":
                ShadersPage.Visibility = Visibility.Visible;
                break;
            case "nova://store":
                StorePage.Visibility = Visibility.Visible;
                RefreshStoreList();
                break;
            case var detail when detail.StartsWith("nova://store/addon/", StringComparison.OrdinalIgnoreCase):
                AddonDetailPage.Visibility = Visibility.Visible;
                ShowAddonDetail(detail["nova://store/addon/".Length..]);
                break;
            default:
                StartPage.Visibility = Visibility.Visible;
                break;
        }

        NotifyCollectionViews();
    }

    private static string StripInternalUrlSuffix(string url)
    {
        var queryIndex = url.IndexOfAny(['?', '#']);
        return queryIndex >= 0 ? url[..queryIndex] : url;
    }

    private static bool IsStartTab(string? url)
    {
        return string.Equals(url, AddressParser.HomeUrl, StringComparison.OrdinalIgnoreCase);
    }

    private void SaveSession()
    {
        _sessionStore.Save(new SessionState
        {
            OpenTabs = Tabs.Select(tab => tab.Url).Where(url => AddressParser.IsInternalUrl(url) || AddressParser.IsWebUrl(url)).Take(12).ToList(),
            ActiveTabUrl = _activeTab?.Url
        });

        _bookmarkService.Save();
        _historyService.Save();
        _downloadService.Save();
        _addonService.Save();
        _settingsService.Save();
    }

    private void UpdateBookmarkButton()
    {
        var isBookmarked = _activeTab is not null && AddressParser.IsWebUrl(_activeTab.Url) && _bookmarkService.Contains(_activeTab.Url);
        BookmarkButton.Content = isBookmarked ? "\uE735" : "\uE734";
    }

    private void UpdateBookmarkBarVisibility()
    {
        var row = RootGrid.RowDefinitions[2];
        row.Height = _settingsService.Current.ShowBookmarkBar ? new GridLength(34) : new GridLength(0);
    }

    private void NotifyCollectionViews()
    {
        OnPropertyChanged(nameof(PinnedExtensions));
        OnPropertyChanged(nameof(RecentHistory));
        OnPropertyChanged(nameof(RecentDownloads));
        OnPropertyChanged(nameof(CurrentExtensionAccessText));
        OnPropertyChanged(nameof(CurrentExtensionAccessHint));
        OnPropertyChanged(nameof(ExtensionAccessToggleBrush));
        OnPropertyChanged(nameof(ExtensionAccessKnobAlignment));
    }

    private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged -= DownloadItem_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<INotifyPropertyChanged>())
            {
                item.PropertyChanged += DownloadItem_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(RecentDownloads));
        UpdateDownloadVisualState();
    }

    private void DownloadItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NovaDownloadItem.Progress) or nameof(NovaDownloadItem.Status) or nameof(NovaDownloadItem.IsActive))
        {
            OnPropertyChanged(nameof(RecentDownloads));
            UpdateDownloadVisualState();
        }
    }

    private void SetLoadingAnimation(bool isLoading)
    {
        if (isLoading)
        {
            if (_reloadStoryboard is not null)
            {
                return;
            }

            ReloadButton.Content = "\uE711";
            ReloadButton.ToolTip = "Laden stoppen";
            ReloadButton.Foreground = new SolidColorBrush(Color.FromRgb(239, 223, 255));
            ReloadButton.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 0,
                Color = Color.FromRgb(185, 103, 255),
                Opacity = 0.72
            };

            var pulse = new DoubleAnimation(0.68, 1, TimeSpan.FromMilliseconds(680))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(pulse, ReloadButton);
            Storyboard.SetTargetProperty(pulse, new PropertyPath(UIElement.OpacityProperty));
            _reloadStoryboard = new Storyboard();
            _reloadStoryboard.Children.Add(pulse);
            _reloadStoryboard.Begin(this, true);

            PageLoadingGlow.Visibility = Visibility.Visible;
            PageLoadingGlow.Opacity = 1;
            PageLoadingScale.ScaleX = 0.05;
            var barScale = new DoubleAnimation(0.05, 1, TimeSpan.FromMilliseconds(950))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(barScale, PageLoadingScale);
            Storyboard.SetTargetProperty(barScale, new PropertyPath(ScaleTransform.ScaleXProperty));

            var barOpacity = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(480))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(barOpacity, PageLoadingGlow);
            Storyboard.SetTargetProperty(barOpacity, new PropertyPath(UIElement.OpacityProperty));

            _loadingBarStoryboard = new Storyboard();
            _loadingBarStoryboard.Children.Add(barScale);
            _loadingBarStoryboard.Children.Add(barOpacity);
            _loadingBarStoryboard.Begin(this, true);

            PageLoadingOverlay.Visibility = Visibility.Visible;
            PageLoadingOverlay.Opacity = 1;
            PageLoadingRingRotate.Angle = 0;
            PageLoadingLogoScale.ScaleX = 1;
            PageLoadingLogoScale.ScaleY = 1;

            var ringSpin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1200))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(ringSpin, PageLoadingRingRotate);
            Storyboard.SetTargetProperty(ringSpin, new PropertyPath(RotateTransform.AngleProperty));

            var logoPulseX = new DoubleAnimation(0.96, 1.05, TimeSpan.FromMilliseconds(760))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(logoPulseX, PageLoadingLogoScale);
            Storyboard.SetTargetProperty(logoPulseX, new PropertyPath(ScaleTransform.ScaleXProperty));

            var logoPulseY = new DoubleAnimation(0.96, 1.05, TimeSpan.FromMilliseconds(760))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(logoPulseY, PageLoadingLogoScale);
            Storyboard.SetTargetProperty(logoPulseY, new PropertyPath(ScaleTransform.ScaleYProperty));

            var overlayBreath = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(620))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(overlayBreath, PageLoadingOverlay);
            Storyboard.SetTargetProperty(overlayBreath, new PropertyPath(UIElement.OpacityProperty));

            _pageLoadingStoryboard = new Storyboard();
            _pageLoadingStoryboard.Children.Add(ringSpin);
            _pageLoadingStoryboard.Children.Add(logoPulseX);
            _pageLoadingStoryboard.Children.Add(logoPulseY);
            _pageLoadingStoryboard.Children.Add(overlayBreath);
            _pageLoadingStoryboard.Begin(this, true);
            return;
        }

        _reloadStoryboard?.Stop(this);
        _reloadStoryboard = null;
        _loadingBarStoryboard?.Stop(this);
        _loadingBarStoryboard = null;
        _pageLoadingStoryboard?.Stop(this);
        _pageLoadingStoryboard = null;
        ReloadButtonSpin.Angle = 0;
        ReloadButton.Content = "\uE72C";
        ReloadButton.ToolTip = "Neu laden";
        ReloadButton.Opacity = 1;
        ReloadButton.Effect = null;
        ReloadButton.Foreground = new SolidColorBrush(Color.FromRgb(192, 140, 255));
        PageLoadingGlow.Visibility = Visibility.Collapsed;
        PageLoadingGlow.Opacity = 0;
        PageLoadingScale.ScaleX = 0.05;
        PageLoadingRingRotate.Angle = 0;
        PageLoadingLogoScale.ScaleX = 1;
        PageLoadingLogoScale.ScaleY = 1;
        PageLoadingOverlay.Opacity = 0;
        PageLoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateDownloadVisualState()
    {
        var activeDownload = Downloads.FirstOrDefault(item => item.IsActive);
        var hasActiveDownload = activeDownload is not null;
        DownloadActiveDot.Visibility = hasActiveDownload ? Visibility.Visible : Visibility.Collapsed;
        DownloadHud.Visibility = hasActiveDownload ? Visibility.Visible : Visibility.Collapsed;

        if (activeDownload is not null)
        {
            DownloadHudFileText.Text = string.IsNullOrWhiteSpace(activeDownload.FileName) ? "Download wird vorbereitet..." : activeDownload.FileName;
            DownloadHudProgress.Value = activeDownload.Progress;
            DownloadHudStatusText.Text = activeDownload.Status;
        }

        if (hasActiveDownload)
        {
            if (_downloadPulseStoryboard is not null)
            {
                return;
            }

            DownloadPulse.Opacity = 0.35;
            DownloadPulseScale.ScaleX = 0.82;
            DownloadPulseScale.ScaleY = 0.82;

            var opacity = new DoubleAnimation(0.25, 0.9, TimeSpan.FromMilliseconds(760))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(opacity, DownloadPulse);
            Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));

            var scaleX = new DoubleAnimation(0.82, 1.15, TimeSpan.FromMilliseconds(760))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(scaleX, DownloadPulseScale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));

            var scaleY = new DoubleAnimation(0.82, 1.15, TimeSpan.FromMilliseconds(760))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(scaleY, DownloadPulseScale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));

            _downloadPulseStoryboard = new Storyboard();
            _downloadPulseStoryboard.Children.Add(opacity);
            _downloadPulseStoryboard.Children.Add(scaleX);
            _downloadPulseStoryboard.Children.Add(scaleY);
            _downloadPulseStoryboard.Begin(this, true);
            return;
        }

        _downloadPulseStoryboard?.Stop(this);
        _downloadPulseStoryboard = null;
        DownloadPulse.Opacity = 0;
        DownloadPulseScale.ScaleX = 0.78;
        DownloadPulseScale.ScaleY = 0.78;
    }

    private static string GetInternalTitle(string url)
    {
        return url.ToLowerInvariant() switch
        {
            "nova://history" => "History",
            "nova://downloads" => "Downloads",
            "nova://diagnostics" => "Diagnostics",
            "nova://media-diagnostics" => "Media Diagnostics",
            "nova://settings" => "Settings",
            var settings when settings.StartsWith("nova://settings/", StringComparison.OrdinalIgnoreCase) => "Settings",
            "nova://update" => "Beta Update",
            "nova://extensions" => "Extensions",
            "nova://mods" => "NX Mods",
            "nova://shaders" => "NX Shader",
            "nova://store" => "NovaStore",
            var detail when detail.StartsWith("nova://store/addon/", StringComparison.OrdinalIgnoreCase) => "Addon Details",
            _ => "Start Page"
        };
    }

    private async Task CheckGoogleBlockedPage(IFrame frame)
    {
        try
        {
            var result = await frame.EvaluateScriptAsync("document.body ? document.body.innerText : ''");
            var text = result.Success ? result.Result?.ToString() ?? "" : "";
            if (text.Contains("Dieser Browser oder diese App ist unter Umstanden nicht sicher", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Dieser Browser oder diese App ist unter Umständen nicht sicher", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("not secure", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => GoogleNotice.Visibility = Visibility.Visible);
            }
        }
        catch
        {
            // Some account pages block script inspection; navigation remains internal and secure.
        }
    }

    private void GoogleAuthTimer_Tick(object? sender, EventArgs e)
    {
        if (_activeTab is { Url: var url } && IsGoogleHost(url))
        {
            QueueGoogleAuthCheck(url);
        }
    }

    private void QueueGoogleAuthCheck(string? url, bool forceToast = false)
    {
        if (!IsGoogleHost(url))
        {
            return;
        }

        _lastGoogleAuthProbeUrl = url;
        _ = CheckGoogleAuthStateAsync(url!, forceToast);
    }

    private async Task CheckGoogleAuthStateAsync(string url, bool forceToast)
    {
        if (_googleAuthCheckRunning)
        {
            return;
        }

        _googleAuthCheckRunning = true;
        try
        {
            var signedIn = await HasGoogleSignInCookiesAsync();
            RunOnUi(() => UpdateGoogleAuthToast(signedIn, url, forceToast), "google-auth-state");
        }
        catch (Exception ex)
        {
            App.LogException("google-auth-state", ex);
        }
        finally
        {
            _googleAuthCheckRunning = false;
        }
    }

    private static async Task<bool> HasGoogleSignInCookiesAsync()
    {
        var manager = Cef.GetGlobalCookieManager();
        if (manager is null)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cookieUrl in new[]
                 {
                     "https://accounts.google.com",
                     "https://www.google.com",
                     "https://youtube.com",
                     "https://www.youtube.com"
                 })
        {
            foreach (var name in await ReadCookieNamesAsync(manager, cookieUrl))
            {
                names.Add(name);
            }
        }

        return names.Contains("SID") ||
               names.Contains("LSID") ||
               names.Contains("SSID") ||
               names.Contains("HSID") ||
               names.Contains("APISID") ||
               names.Contains("SAPISID") ||
               names.Contains("__Secure-1PSID") ||
               names.Contains("__Secure-3PSID");
    }

    private static async Task<HashSet<string>> ReadCookieNamesAsync(ICookieManager manager, string url)
    {
        var visitor = new CookieNameVisitor();
        if (!manager.VisitUrlCookies(url, includeHttpOnly: true, visitor))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return await visitor.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void UpdateGoogleAuthToast(bool signedIn, string url, bool forceToast)
    {
        if (!IsGoogleHost(url))
        {
            return;
        }

        var changed = _lastGoogleSignedIn != signedIn;
        _lastGoogleSignedIn = signedIn;
        if (!changed && !forceToast)
        {
            return;
        }

        GoogleAuthToastTitle.Text = signedIn ? "Google angemeldet" : "Google nicht angemeldet";
        GoogleAuthToastHint.Text = signedIn
            ? "Nova erkennt die echte Google-Sitzung in der persistenten Browser-Session."
            : "Nova wartet auf die Google-Anmeldung und prueft die Sitzung live.";
        StatusText.Text = signedIn ? "Google-Anmeldung aktiv." : "Google-Anmeldung noch nicht aktiv.";
        ShowGoogleAuthToast();
    }

    private void ShowGoogleAuthToast()
    {
        GoogleAuthToast.Visibility = Visibility.Visible;
        GoogleAuthToast.BeginAnimation(OpacityProperty, null);
        GoogleAuthToast.Opacity = 1;

        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(520))
        {
            BeginTime = TimeSpan.FromSeconds(3.2),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) => GoogleAuthToast.Visibility = Visibility.Collapsed;
        GoogleAuthToast.BeginAnimation(OpacityProperty, animation);
    }

    private void CheckGoogleBlockedAddress(string address)
    {
        if (address.Contains("/signin/rejected", StringComparison.OrdinalIgnoreCase) ||
            address.Contains("notsecure", StringComparison.OrdinalIgnoreCase))
        {
            GoogleNotice.Visibility = Visibility.Visible;
        }
    }

    private static bool IsGoogleHost(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host == "google.com" ||
               host.EndsWith(".google.com") ||
               host == "youtube.com" ||
               host.EndsWith(".youtube.com") ||
               host == "accounts.google.com" ||
               host == "myaccount.google.com" ||
               host == "oauth.google.com" ||
               host == "signin.google.com";
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        OpenInNewTab(AddressParser.HomeUrl);
    }
    private void Home_Click(object sender, RoutedEventArgs e) => NavigateActive(AddressParser.HomeUrl);
    private void Go_Click(object sender, RoutedEventArgs e) => NavigateActive(AddressBox.Text);

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab || e.Key == Key.Right)
        {
            if (TryAcceptOmniboxAutocomplete())
            {
                e.Handled = true;
                return;
            }
        }

        if (OmniboxPopup.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Down)
            {
                var next = OmniboxSuggestionsList.SelectedIndex < OmniboxSuggestions.Count - 1
                    ? OmniboxSuggestionsList.SelectedIndex + 1
                    : 0;
                OmniboxSuggestionsList.SelectedIndex = next;
                OmniboxSuggestionsList.ScrollIntoView(OmniboxSuggestionsList.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                var previous = OmniboxSuggestionsList.SelectedIndex > 0
                    ? OmniboxSuggestionsList.SelectedIndex - 1
                    : OmniboxSuggestions.Count - 1;
                OmniboxSuggestionsList.SelectedIndex = previous;
                OmniboxSuggestionsList.ScrollIntoView(OmniboxSuggestionsList.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                CloseOmniboxSuggestions();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter)
        {
            if (OmniboxPopup.Visibility == Visibility.Visible &&
                OmniboxSuggestionsList.SelectedItem is OmniboxSuggestion suggestion)
            {
                NavigateActive(suggestion.Target);
            }
            else
            {
                NavigateActive(AddressBox.Text);
            }

            e.Handled = true;
        }
    }

    private void AddressBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressOmniboxSuggestions || _isApplyingOmniboxAutocomplete || !AddressBox.IsKeyboardFocusWithin)
        {
            return;
        }

        var isDeletion = e.Changes.Count > 0 && e.Changes.All(change => change.RemovedLength > 0 && change.AddedLength == 0);
        RefreshOmniboxSuggestions(AddressBox.Text, applyAutocomplete: !isDeletion);
    }

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressBox.SelectAll();
        RefreshOmniboxSuggestions(AddressBox.Text, applyAutocomplete: false);
    }

    private void OmniboxSuggestionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OmniboxSuggestionsList.SelectedItem is OmniboxSuggestion suggestion)
        {
            NavigateActive(suggestion.Target);
        }
    }

    private void OmniboxSuggestion_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBoxItem)?.DataContext is OmniboxSuggestion suggestion)
        {
            NavigateActive(suggestion.Target);
            e.Handled = true;
        }
    }

    private void OmniboxSuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void RefreshOmniboxSuggestions(string input, bool applyAutocomplete = false)
    {
        var query = (input ?? "").Trim();
        OmniboxSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var item in History.Take(6))
            {
                OmniboxSuggestions.Add(OmniboxSuggestion.FromHistory(item));
            }
        }
        else
        {
            var normalized = AddressParser.Normalize(query, _settingsService.Current.SearchEngine);
            if (AddressParser.IsWebUrl(normalized) || AddressParser.IsInternalUrl(normalized))
            {
                OmniboxSuggestions.Add(new OmniboxSuggestion("\uE774", query, normalized, normalized, "Oeffnen"));
            }

            foreach (var tab in Tabs
                         .Where(tab => MatchesOmniboxQuery(tab.Title, tab.Url, query))
                         .OrderByDescending(tab => ReferenceEquals(tab, _activeTab))
                         .Take(3))
            {
                OmniboxSuggestions.Add(OmniboxSuggestion.FromTab(tab));
            }

            foreach (var bookmark in Bookmarks
                         .Where(item => MatchesOmniboxQuery(item.Title, item.Url, query))
                         .OrderByDescending(item => StartsWithOmniboxQuery(item.Title, item.Url, query))
                         .ThenByDescending(item => item.CreatedAt)
                         .Take(5))
            {
                OmniboxSuggestions.Add(OmniboxSuggestion.FromBookmark(bookmark));
            }

            foreach (var item in History
                         .Where(item => MatchesOmniboxQuery(item.Title, item.Url, query))
                         .OrderByDescending(item => StartsWithOmniboxQuery(item.Title, item.Url, query))
                         .ThenByDescending(item => item.VisitedAt)
                         .Take(8))
            {
                OmniboxSuggestions.Add(OmniboxSuggestion.FromHistory(item));
            }

            OmniboxSuggestions.Add(new OmniboxSuggestion("\uE721", query, $"Suche mit {_settingsService.Current.SearchEngine}", query, "Suche"));

            foreach (var suggestion in BuildSearchCompletions(query).Take(4))
            {
                OmniboxSuggestions.Add(new OmniboxSuggestion("\uE721", suggestion, "Suchvorschlag", suggestion, ""));
            }
        }

        var unique = OmniboxSuggestions
            .GroupBy(item => item.Target, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToList();

        OmniboxSuggestions.Clear();
        foreach (var item in unique)
        {
            OmniboxSuggestions.Add(item);
        }

        OmniboxSuggestionsList.SelectedIndex = OmniboxSuggestions.Count > 0 ? 0 : -1;
        var hasSuggestions = OmniboxSuggestions.Count > 0;
        OmniboxPopup.Visibility = hasSuggestions ? Visibility.Visible : Visibility.Collapsed;
        OmniboxPopupHost.IsOpen = hasSuggestions;

        if (applyAutocomplete)
        {
            ApplyOmniboxAutocomplete(query, unique);
        }
    }

    private void ApplyOmniboxAutocomplete(string query, IReadOnlyList<OmniboxSuggestion> suggestions)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            AddressBox.SelectionLength > 0 ||
            AddressBox.CaretIndex != AddressBox.Text.Length ||
            query.Contains(' '))
        {
            return;
        }

        var completion = suggestions
            .SelectMany(item => new[] { item.DisplayCompletion, item.Target })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(value => value.StartsWith(query, StringComparison.OrdinalIgnoreCase) &&
                                     value.Length > query.Length &&
                                     !value.StartsWith("https://www.google.com/search", StringComparison.OrdinalIgnoreCase) &&
                                     !value.StartsWith("https://duckduckgo.com/", StringComparison.OrdinalIgnoreCase) &&
                                     !value.StartsWith("https://www.bing.com/search", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(completion))
        {
            return;
        }

        _isApplyingOmniboxAutocomplete = true;
        AddressBox.Text = completion;
        AddressBox.SelectionStart = query.Length;
        AddressBox.SelectionLength = completion.Length - query.Length;
        _isApplyingOmniboxAutocomplete = false;
    }

    private bool TryAcceptOmniboxAutocomplete()
    {
        if (!AddressBox.IsKeyboardFocusWithin || AddressBox.SelectionLength <= 0)
        {
            return false;
        }

        AddressBox.CaretIndex = AddressBox.Text.Length;
        AddressBox.SelectionLength = 0;
        RefreshOmniboxSuggestions(AddressBox.Text, applyAutocomplete: false);
        return true;
    }

    private static bool MatchesOmniboxQuery(string title, string url, string query)
    {
        return title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               SimplifyUrlForCompletion(url).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithOmniboxQuery(string title, string url, string query)
    {
        return title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
               SimplifyUrlForCompletion(url).StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string SimplifyUrlForCompletion(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return host + uri.PathAndQuery.TrimEnd('/');
    }

    private static IEnumerable<string> BuildSearchCompletions(string query)
    {
        if (query.Contains(' '))
        {
            yield break;
        }

        yield return query + " youtube";
        yield return query + " download";
        yield return query + " github";
        yield return query + " wiki";
    }

    private void CloseOmniboxSuggestions()
    {
        OmniboxPopup.Visibility = Visibility.Collapsed;
        OmniboxPopupHost.IsOpen = false;
        OmniboxSuggestions.Clear();
    }

    private void StartSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateActive(StartSearchBox.Text);
        }
    }

    private void StartSearch_Click(object sender, RoutedEventArgs e) => NavigateActive(StartSearchBox.Text);

    private void StartSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (StartSearchBox.Text == "Suche oder URL eingeben")
        {
            StartSearchBox.Clear();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var browser = _activeTab?.Browser;
        if (browser is null)
        {
            return;
        }

        if (browser.CanGoBack)
        {
            browser.Back();
            StatusText.Text = "Zurueck...";
            return;
        }

        _ = TryHistoryFallbackAsync(browser, "back");
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        var browser = _activeTab?.Browser;
        if (browser is null)
        {
            return;
        }

        if (browser.CanGoForward)
        {
            browser.Forward();
            StatusText.Text = "Vorwaerts...";
            return;
        }

        _ = TryHistoryFallbackAsync(browser, "forward");
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        if (_activeTab.Browser?.IsLoading == true)
        {
            _activeTab.Browser.Stop();
            SetLoadingAnimation(false);
            StatusText.Text = "Laden gestoppt.";
            return;
        }

        if (IsStartTab(_activeTab.Url))
        {
            _activeTab.Browser?.Reload();
        }
        else if (AddressParser.IsInternalUrl(_activeTab.Url))
        {
            ShowInternalPage(_activeTab.Url);
        }
        else
        {
            ReloadWebTab(ignoreCache: true);
        }
    }

    private async Task TryHistoryFallbackAsync(ChromiumWebBrowser browser, string direction)
    {
        if (_activeTab is null || !AddressParser.IsWebUrl(_activeTab.Url))
        {
            StatusText.Text = direction == "back" ? "Keine vorherige Seite." : "Keine naechste Seite.";
            return;
        }

        try
        {
            var script = direction == "back"
                ? "if (history.length > 1) { history.back(); 'back'; } else { 'empty'; }"
                : "history.forward(); 'forward';";
            var result = await browser.EvaluateScriptAsync(script);
            StatusText.Text = result.Success ? (direction == "back" ? "Zurueck..." : "Vorwaerts...") : "Navigation nicht moeglich.";
        }
        catch (Exception ex)
        {
            App.LogException("history-fallback", ex);
            StatusText.Text = "Navigation konnte nicht ausgefuehrt werden.";
        }
    }

    private void ReloadWebTab(bool ignoreCache)
    {
        if (_activeTab?.Browser is not { } browser || !AddressParser.IsWebUrl(_activeTab.Url))
        {
            return;
        }

        try
        {
            SlowLoadBar.Visibility = Visibility.Collapsed;
            if (browser.IsLoading)
            {
                browser.Stop();
            }

            browser.Reload(ignoreCache);
            StatusText.Text = ignoreCache ? "Wird frisch geladen..." : "Wird neu geladen...";
        }
        catch (Exception ex)
        {
            App.LogException("reload-web-tab", ex);
            browser.Load(_activeTab.Url);
            StatusText.Text = "Reload-Fallback gestartet.";
        }
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideType<Button>(source))
        {
            return;
        }

        if (sender is FrameworkElement { Tag: BrowserTab tab })
        {
            _tabDragCandidate = tab;
            _tabDragStartPoint = e.GetPosition(this);
            SelectTab(tab);
            e.Handled = true;
        }
    }

    private void Tab_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _tabDragCandidate is null ||
            sender is not FrameworkElement { Tag: BrowserTab tab } ||
            !ReferenceEquals(tab, _tabDragCandidate))
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _tabDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _tabDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, tab, DragDropEffects.Move);
        _tabDragCandidate = null;
    }

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(BrowserTab)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BrowserTab targetTab } ||
            e.Data.GetData(typeof(BrowserTab)) is not BrowserTab draggedTab ||
            ReferenceEquals(targetTab, draggedTab))
        {
            return;
        }

        var oldIndex = Tabs.IndexOf(draggedTab);
        var newIndex = Tabs.IndexOf(targetTab);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        Tabs.Move(oldIndex, newIndex);
        SelectTab(draggedTab);
        e.Handled = true;
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BrowserTab tab })
        {
            return;
        }

        CloseTab(tab);
        e.Handled = true;
    }

    private void CloseActiveTab()
    {
        if (_activeTab is not null)
        {
            CloseTab(_activeTab);
        }
    }

    private void CloseTab(BrowserTab tab)
    {
        var wasActive = ReferenceEquals(tab, _activeTab);
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        if (wasActive)
        {
            BrowserTab nextTab;
            if (Tabs.Count == 1)
            {
                nextTab = CreateTab(AddressParser.HomeUrl, select: false);
            }
            else
            {
                var nextIndex = index < Tabs.Count - 1 ? index + 1 : index - 1;
                nextTab = Tabs[nextIndex];
            }

            SelectTab(nextTab);
        }

        Tabs.Remove(tab);
        _tabLastActiveAt.Remove(tab);
        _snoozedTabUrls.Remove(tab);
        SafeDisposeBrowser(tab, "close-tab");
    }

    private static void SafeDisposeBrowser(BrowserTab tab, string source)
    {
        try
        {
            tab.Browser?.Dispose();
        }
        catch (Exception ex)
        {
            App.LogException(source, ex);
        }
        finally
        {
            tab.Browser = null;
        }
    }

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        ToggleBookmarkPopup();
    }

    private void BookmarkButtonShell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ToggleBookmarkPopup();
        e.Handled = true;
    }

    private void ToggleBookmarkPopup()
    {
        if (_bookmarkFlyoutWindow is not null)
        {
            _bookmarkFlyoutWindow.Close();
            _bookmarkFlyoutWindow = null;
            StatusText.Text = "Lesezeichen geschlossen.";
            return;
        }

        ShowBookmarkFlyout();
    }

    private void ShowBookmarkFlyout()
    {
        CloseTransientPanels();

        var isWebPage = _activeTab is not null && AddressParser.IsWebUrl(_activeTab.Url);
        var existing = isWebPage ? _bookmarkService.Find(_activeTab!.Url) : null;

        var titleBox = new TextBox
        {
            Height = 34,
            Text = isWebPage ? existing?.Title ?? _activeTab!.Title : _activeTab?.Title ?? "Nova",
            IsEnabled = isWebPage,
            Margin = new Thickness(0, 4, 0, 10)
        };
        var folderBox = new ComboBox
        {
            Height = 34,
            Text = existing?.Folder ?? "Bookmarks",
            IsEditable = true,
            IsEnabled = isWebPage,
            Margin = new Thickness(0, 4, 0, 14)
        };
        foreach (var folder in new[] { "Bookmarks", "Work", "Dev Resources", "Design Inspiration" })
        {
            folderBox.Items.Add(folder);
        }

        var saveButton = CreateFlyoutActionButton("Fertig");
        saveButton.IsEnabled = isWebPage;
        saveButton.Click += (_, _) =>
        {
            if (_activeTab is null || !AddressParser.IsWebUrl(_activeTab.Url))
            {
                return;
            }

            var title = string.IsNullOrWhiteSpace(titleBox.Text) ? _activeTab.Title : titleBox.Text.Trim();
            var folder = string.IsNullOrWhiteSpace(folderBox.Text) ? "Bookmarks" : folderBox.Text.Trim();
            _bookmarkService.SaveOrUpdate(title, _activeTab.Url, folder);
            UpdateBookmarkButton();
            CloseBookmarkFlyout();
            StatusText.Text = "Lesezeichen gespeichert.";
        };

        var removeButton = CreateFlyoutActionButton("Entfernen");
        removeButton.IsEnabled = existing is not null;
        removeButton.Click += (_, _) =>
        {
            if (_activeTab is not null)
            {
                var item = _bookmarkService.Find(_activeTab.Url);
                if (item is not null)
                {
                    _bookmarkService.Remove(item);
                }
            }

            UpdateBookmarkButton();
            CloseBookmarkFlyout();
            StatusText.Text = "Lesezeichen entfernt.";
        };

        var content = CreateFlyoutPanel(320);
        var stack = new StackPanel();
        content.Child = stack;
        stack.Children.Add(new TextBlock { Text = "Lesezeichen bearbeiten", Foreground = FindBrush("NovaText"), FontSize = 18, FontWeight = FontWeights.Black });
        stack.Children.Add(new TextBlock
        {
            Text = isWebPage ? existing is null ? "Diese Webseite als Lesezeichen speichern." : "Diese Webseite ist bereits gespeichert." : "Interne Nova-Seiten koennen nicht als Lesezeichen gespeichert werden.",
            Foreground = FindBrush("MutedText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8)
        });
        stack.Children.Add(new TextBlock { Text = "Name", Foreground = FindBrush("MutedText") });
        stack.Children.Add(titleBox);
        stack.Children.Add(new TextBlock { Text = "Ordner", Foreground = FindBrush("MutedText") });
        stack.Children.Add(folderBox);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { removeButton, saveButton }
        });

        _bookmarkFlyoutWindow = ShowFlyoutWindow(content, BookmarkButton, 320);
        _bookmarkFlyoutWindow.Closed += (_, _) => _bookmarkFlyoutWindow = null;
        StatusText.Text = isWebPage ? "Stern geoeffnet." : "Stern geoeffnet: interne Seite.";
    }

    private void SaveBookmarkFromPopup_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null || !AddressParser.IsWebUrl(_activeTab.Url))
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(BookmarkNameBox.Text) ? _activeTab.Title : BookmarkNameBox.Text.Trim();
        var folder = string.IsNullOrWhiteSpace(BookmarkFolderBox.Text) ? "Bookmarks" : BookmarkFolderBox.Text.Trim();
        _bookmarkService.SaveOrUpdate(title, _activeTab.Url, folder);
        BookmarkPopup.Visibility = Visibility.Collapsed;
        UpdateBookmarkButton();
        StatusText.Text = "Lesezeichen gespeichert.";
    }

    private void RemoveBookmarkFromPopup_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is null)
        {
            return;
        }

        var existing = _bookmarkService.Find(_activeTab.Url);
        if (existing is not null)
        {
            _bookmarkService.Remove(existing);
        }

        BookmarkPopup.Visibility = Visibility.Collapsed;
        UpdateBookmarkButton();
        StatusText.Text = "Lesezeichen entfernt.";
    }

    private void BookmarkBar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Bookmark bookmark })
        {
            OpenInNewTab(bookmark.Url);
        }
    }

    private void QuickLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            NavigateActive(url);
        }
    }

    private void AddLink_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not null && AddressParser.IsWebUrl(_activeTab.Url))
        {
            _bookmarkService.Toggle(_activeTab.Title, _activeTab.Url);
            StatusText.Text = "Aktuelle Seite als Quick Bookmark gespeichert.";
        }
        else
        {
            StatusText.Text = "Oeffne erst eine Webseite, dann kann Nova sie speichern.";
        }
    }

    private void HistoryOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HistoryItem item })
        {
            OpenInNewTab(item.Url);
        }
    }

    private void HistoryList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: HistoryItem item })
        {
            OpenInNewTab(item.Url);
        }
    }

    private void HistorySearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (HistorySearchBox.Text == "Verlauf durchsuchen")
        {
            HistorySearchBox.Clear();
        }
    }

    private void HistorySearchBox_KeyUp(object sender, KeyEventArgs e)
    {
        var query = HistorySearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query) || query == "Verlauf durchsuchen")
        {
            HistoryListBox.ItemsSource = History;
            return;
        }

        HistoryListBox.ItemsSource = History
            .Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _historyService.Clear();
        NotifyCollectionViews();
    }

    private void RemoveHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HistoryItem item })
        {
            _historyService.Remove(item);
            NotifyCollectionViews();
        }
    }

    private void OpenDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NovaDownloadItem item })
        {
            DownloadService.Open(item);
        }
    }

    private void ShowDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NovaDownloadItem item })
        {
            DownloadService.ShowInFolder(item);
        }
    }

    private void RemoveDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NovaDownloadItem item })
        {
            _downloadService.Remove(item);
            NotifyCollectionViews();
        }
    }

    private void ExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionsPopupHost.IsOpen || ExtensionsPopup.Visibility == Visibility.Visible)
        {
            ExtensionsPopup.Visibility = Visibility.Collapsed;
            ExtensionsPopupHost.IsOpen = false;
            StatusText.Text = "Erweiterungen geschlossen.";
            return;
        }

        CloseTransientPanels();
        UpdateExtensionAccessState();
        RefreshExtensionsPopup();
        ExtensionsPopup.Visibility = Visibility.Visible;
        ExtensionsPopupHost.IsOpen = true;
        StatusText.Text = "Erweiterungen geoeffnet.";
    }

    private void CloseExtensionsPopup_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.Visibility = Visibility.Collapsed;
        ExtensionsPopupHost.IsOpen = false;
    }

    private void ExtensionAccessToggle_Click(object sender, RoutedEventArgs e)
    {
        var domain = GetActiveExtensionDomain();
        var siteSettings = GetAddonSiteAccessSettings();
        ExtensionsAllowedForCurrentSite = !ExtensionsAllowedForCurrentSite;
        siteSettings[domain] = ExtensionsAllowedForCurrentSite ? "true" : "false";
        _addonService.Save();
        UpdateExtensionAccessState();
        StatusText.Text = ExtensionsAllowedForCurrentSite
            ? $"Erweiterungen duerfen auf {domain} arbeiten."
            : $"Erweiterungen sind auf {domain} blockiert.";
    }

    private void OpenExtensionsPage_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsPopup.Visibility = Visibility.Collapsed;
        NavigateActive("nova://extensions");
    }

    private void PinnedExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem item })
        {
            ShowAddonActionPopup(item);
        }
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (MenuPanel.Visibility == Visibility.Visible)
        {
            MenuPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Menue geschlossen.";
            return;
        }

        CloseTransientPanels();
        MenuPanel.Visibility = Visibility.Visible;
        StatusText.Text = "Menue geoeffnet.";
    }

    private void MainMenu_NewTabRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        OpenInNewTab(AddressParser.HomeUrl);
    }

    private void MainMenu_NewWindowRequested(object sender, EventArgs e) => NewWindow_Click(sender, new RoutedEventArgs());

    private void MainMenu_IncognitoRequested(object sender, EventArgs e) => Incognito_Click(sender, new RoutedEventArgs());

    private void MainMenu_ProfileRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        NavigateActive("nova://settings");
        StatusText.Text = "Profilbereich in den Einstellungen geoeffnet.";
    }

    private void MainMenu_PasswordsRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        NavigateActive("nova://settings");
        StatusText.Text = "Passwoerter und Autofill werden in den Einstellungen vorbereitet.";
    }

    private void MainMenu_HistoryRequested(object sender, EventArgs e) => History_Click(sender, new RoutedEventArgs());

    private void MainMenu_DownloadsRequested(object sender, EventArgs e) => Downloads_Click(sender, new RoutedEventArgs());

    private void MainMenu_BookmarksRequested(object sender, EventArgs e) => BookmarksMenu_Click(sender, new RoutedEventArgs());

    private void MainMenu_TabGroupsRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        StatusText.Text = "Tabgruppen sind im Nova-Menue vorbereitet.";
    }

    private void MainMenu_ExtensionsRequested(object sender, EventArgs e) => ExtensionsMenu_Click(sender, new RoutedEventArgs());

    private void MainMenu_ClearDataRequested(object sender, EventArgs e) => ClearBrowserData_Click(sender, new RoutedEventArgs());

    private void MainMenu_ZoomOutRequested(object sender, EventArgs e) => ZoomOut_Click(sender, new RoutedEventArgs());

    private void MainMenu_ZoomInRequested(object sender, EventArgs e) => ZoomIn_Click(sender, new RoutedEventArgs());

    private void MainMenu_FullscreenRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        ToggleFullscreen();
    }

    private void MainMenu_PrintRequested(object sender, EventArgs e) => Print_Click(sender, new RoutedEventArgs());

    private void MainMenu_LensRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        if (_activeTab is null || !AddressParser.IsWebUrl(_activeTab.Url))
        {
            StatusText.Text = "Oeffne zuerst eine Webseite fuer Google Lens.";
            return;
        }

        NavigateActive($"https://lens.google.com/uploadbyurl?url={Uri.EscapeDataString(_activeTab.Url)}");
    }

    private void MainMenu_TranslateRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        if (_activeTab is null || !AddressParser.IsWebUrl(_activeTab.Url))
        {
            StatusText.Text = "Oeffne zuerst eine Webseite zum Uebersetzen.";
            return;
        }

        NavigateActive($"https://translate.google.com/translate?sl=auto&tl=de&u={Uri.EscapeDataString(_activeTab.Url)}");
    }

    private void MainMenu_FindEditRequested(object sender, EventArgs e)
    {
        ShowFindBar();
    }

    private void MainMenu_ShareRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        CopyCurrentUrlToClipboard();
    }

    private void MainMenu_MoreToolsRequested(object sender, EventArgs e) => MoreTools_Click(sender, new RoutedEventArgs());

    private void MainMenu_StoreRequested(object sender, EventArgs e) => OpenStore_Click(sender, new RoutedEventArgs());

    private void MainMenu_DevToolsRequested(object sender, EventArgs e) => OpenDevTools_Click(sender, new RoutedEventArgs());

    private void MainMenu_DiagnosticsRequested(object sender, EventArgs e) => OpenDiagnostics_Click(sender, new RoutedEventArgs());

    private void MainMenu_MediaDiagnosticsRequested(object sender, EventArgs e) => OpenMediaDiagnostics_Click(sender, new RoutedEventArgs());

    private void MainMenu_SavePageRequested(object sender, EventArgs e) => SavePage_Click(sender, new RoutedEventArgs());

    private void MainMenu_CopyLinkRequested(object sender, EventArgs e)
    {
        CloseTransientPanels();
        CopyCurrentUrlToClipboard();
    }

    private void MainMenu_HelpRequested(object sender, EventArgs e) => Help_Click(sender, new RoutedEventArgs());

    private void MainMenu_SettingsRequested(object sender, EventArgs e) => Settings_Click(sender, new RoutedEventArgs());

    private void MainMenu_ExitRequested(object sender, EventArgs e) => Exit_Click(sender, new RoutedEventArgs());

    private void History_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels(HistorySidePanel);
        SideHistoryListBox.ItemsSource = History;
        HistorySidePanel.Visibility = HistorySidePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        StatusText.Text = HistorySidePanel.Visibility == Visibility.Visible ? "Verlauf geoeffnet." : "Verlauf geschlossen.";
    }

    private void CloseHistorySidePanel_Click(object sender, RoutedEventArgs e)
    {
        HistorySidePanel.Visibility = Visibility.Collapsed;
    }

    private void OpenFullHistory_Click(object sender, RoutedEventArgs e)
    {
        HistorySidePanel.Visibility = Visibility.Collapsed;
        NavigateActive("nova://history");
    }

    private void SideHistorySearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SideHistorySearchBox.Text == "Verlauf durchsuchen")
        {
            SideHistorySearchBox.Clear();
        }
    }

    private void SideHistorySearchBox_KeyUp(object sender, KeyEventArgs e)
    {
        var query = SideHistorySearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query) || query == "Verlauf durchsuchen")
        {
            SideHistoryListBox.ItemsSource = History;
            return;
        }

        SideHistoryListBox.ItemsSource = History
            .Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void SideHistoryList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: HistoryItem item })
        {
            HistorySidePanel.Visibility = Visibility.Collapsed;
            NavigateActive(item.Url);
        }
    }

    private void TelegramBot_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels(TelegramBotPanel);
        if (TelegramBotPanel.Visibility == Visibility.Visible)
        {
            AnimateTelegramBotPanel(show: false);
        }
        else
        {
            AnimateTelegramBotPanel(show: true);
        }

        TelegramBotStatusText.Text = _telegramBotService.HasStoredToken
            ? "Token lokal verschluesselt gespeichert."
            : "Bot Token eintragen, Chat ID setzen und speichern.";
    }

    private void TelegramBotChangeTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var type = GetTelegramBotChangeType();
        TelegramBotStatusText.Text = $"Art der Aenderung: {type}.";
    }

    private string GetTelegramBotChangeType()
    {
        return (TelegramBotChangeTypeBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Sonstiges";
    }

    private string GetTelegramBotMode()
    {
        // Fallback bewusst "Browser-Screenshot" (navigiert NICHT weg), nicht "Startseite":
        // sonst wuerde eine leere Auswahl den aktiven Tab ungewollt auf nova://start schicken.
        return (TelegramBotModeBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Browser-Screenshot";
    }

    private void CloseTelegramBotPanel_Click(object sender, RoutedEventArgs e)
    {
        AnimateTelegramBotPanel(show: false);
    }

    private void AnimateTelegramBotPanel(bool show)
    {
        const int durationMs = 220;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (show)
        {
            TelegramBotPanel.Visibility = Visibility.Visible;
        }

        var opacity = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease
        };
        var slide = new DoubleAnimation
        {
            To = show ? 0 : -28,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = ease
        };

        if (!show)
        {
            opacity.Completed += (_, _) =>
            {
                if (TelegramBotPanel.Opacity <= 0.02)
                {
                    TelegramBotPanel.Visibility = Visibility.Collapsed;
                }
            };
        }

        TelegramBotPanel.BeginAnimation(OpacityProperty, opacity);
        TelegramBotPanelTransform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void SaveTelegramBotSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveTelegramBotSettings();
        TelegramBotStatusText.Text = "Bot-Einstellungen gespeichert.";
    }

    private void TelegramBotMessageBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TelegramBotMessageBox.Text == "Schreibe hier, was geaendert wurde...")
        {
            TelegramBotMessageBox.Clear();
        }
    }

    private void TelegramBotModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TelegramBotModeBox is null || TelegramBotCredentialPanel is null || TelegramBotModeInfoText is null)
        {
            return;
        }

        var mode = (TelegramBotModeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        TelegramBotCredentialPanel.Visibility = mode == "Telegram Zugang" ? Visibility.Visible : Visibility.Collapsed;

        TelegramBotModeInfoText.Text = mode switch
        {
            "Startseite" => "Browser-Screenshot oeffnet zuerst nova://start und nimmt die Startseite auf.",
            "Toolbar & Adressleiste" => "Browser-Screenshot zeigt Toolbar und Adressleiste so, wie sie gerade aussehen.",
            "Tabs & Fenster" => "Browser-Screenshot zeigt die aktuelle Tableiste und Fensteranordnung.",
            "Drei-Punkte-Menue" => "Oeffne das Hauptmenue manuell, dann Browser-Screenshot fuer eine Aufnahme mit offenem Menue.",
            "Erweiterungen & Addons" => "Browser-Screenshot oeffnet zuerst nova://extensions und nimmt die Addon-Seite auf.",
            "Downloads" => "Browser-Screenshot oeffnet zuerst nova://downloads und nimmt die Download-Seite auf.",
            "Verlauf" => "Browser-Screenshot oeffnet zuerst nova://history und nimmt die Verlaufsseite auf.",
            "Einstellungen" => "Browser-Screenshot oeffnet zuerst nova://settings und nimmt die Einstellungsseite auf.",
            "Layouts & Design" => "Browser-Screenshot oeffnet die Design-Bereiche in nova://settings.",
            "Media & Webseiten" => "Browser-Screenshot oeffnet zuerst nova://media-diagnostics und nimmt die Media-Diagnose auf.",
            "Ersteller & Rollen" => "Zeigt die Namen und Rollen des NyxNova-Projekts.",
            "Telegram Zugang" => "Nur hier werden Token und Channel-Ziel angezeigt oder geaendert.",
            _ => "Bereich auswaehlen, dann Browser-Screenshot fuer eine Aufnahme dieses Bereichs."
        };

        if (mode == "Ersteller & Rollen")
        {
            ShowTelegramCreatorRoles();
        }
    }

    private static string? GetTelegramBotModeTargetPage(string mode) => mode switch
    {
        "Erweiterungen & Addons" => "nova://extensions",
        "Downloads" => "nova://downloads",
        "Verlauf" => "nova://history",
        "Einstellungen" => "nova://settings",
        "Startseite" => "nova://start",
        "Layouts & Design" => "nova://settings",
        "Media & Webseiten" => "nova://media-diagnostics",
        _ => null
    };

    private void ShowTelegramCreatorRoles()
    {
        TelegramBotChanges.Clear();
        TelegramBotChanges.Add(new TelegramBotChange
        {
            Title = "Dennis",
            Detail = "Projektinhaber, Design-Entscheidungen, Tests und Release-Freigabe.",
            TargetUrl = AddressParser.HomeUrl,
            Icon = "\uE77B"
        });
        TelegramBotChanges.Add(new TelegramBotChange
        {
            Title = "NyxNova Team",
            Detail = "Browser-Design, Funktionen, Addons, Installer und Update-System.",
            TargetUrl = AddressParser.HomeUrl,
            Icon = "\uE902"
        });
        TelegramBotChanges.Add(new TelegramBotChange
        {
            Title = "Community Tester",
            Detail = "Feedback, Fehlerberichte und Beta-Tests fuer neue Versionen.",
            TargetUrl = AddressParser.HomeUrl,
            Icon = "\uE716"
        });

        TelegramBotSummaryText.Text = "Ersteller und Rollen sind geladen. Du kannst sie mit deinem Update-Text mitsenden.";
        TelegramBotStatusText.Text = "Ersteller/Rollen angezeigt.";
    }

    private async void TelegramBotAnalyze_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeCurrentPageForTelegramAsync(saveSnapshot: true);
    }

    private async void TelegramBotCaptureBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var mode = GetTelegramBotMode();
            var changeType = GetTelegramBotChangeType();
            var targetPage = GetTelegramBotModeTargetPage(mode);

            // Merken, wo der Nutzer gerade war, damit wir nach der Aufnahme zurueckkehren
            // und ihn nicht auf der Zielseite (z. B. nova://start) stehen lassen.
            var previousUrl = _activeTab?.Url;

            if (!string.IsNullOrWhiteSpace(targetPage))
            {
                TelegramBotStatusText.Text = $"Oeffne {mode} ...";
                NavigateActive(targetPage);
                await Task.Delay(360);
            }

            TelegramBotStatusText.Text = "Browser-Screenshot wird erstellt ...";
            _lastTelegramBotScreenshot = await CaptureActiveViewPngAsync();

            _lastTelegramBotSnapshot = CreateLocalTelegramSnapshot($"NyxNova: {mode}", targetPage ?? "");
            SetTelegramBotLocalChange(mode, $"Art: {changeType}");

            // Zuruecknavigieren, falls wir fuer den Screenshot die Seite gewechselt haben.
            if (!string.IsNullOrWhiteSpace(targetPage) &&
                !string.IsNullOrWhiteSpace(previousUrl) &&
                !string.Equals(previousUrl, targetPage, StringComparison.OrdinalIgnoreCase))
            {
                NavigateActive(previousUrl);
            }

            TelegramBotStatusText.Text = "Screenshot ist bereit. Du kannst ihn jetzt senden.";
        }
        catch (Exception ex)
        {
            TelegramBotStatusText.Text = $"Screenshot fehlgeschlagen: {ex.Message}";
        }
    }

    private void TelegramBotPickImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Screenshot oder Bild fuer Telegram auswaehlen",
                Filter = "Bilder (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Alle Dateien (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var mode = GetTelegramBotMode();
            var changeType = GetTelegramBotChangeType();
            _lastTelegramBotScreenshot = System.IO.File.ReadAllBytes(dialog.FileName);
            _lastTelegramBotSnapshot = CreateLocalTelegramSnapshot($"NyxNova: {mode}", GetTelegramBotModeTargetPage(mode) ?? "");
            SetTelegramBotLocalChange(mode, $"Art: {changeType}");
            TelegramBotStatusText.Text = "Bild ist bereit. Du kannst es jetzt senden.";
        }
        catch (Exception ex)
        {
            TelegramBotStatusText.Text = $"Bildauswahl fehlgeschlagen: {ex.Message}";
        }
    }

    private async void TelegramBotSend_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveTelegramBotSettings();

            if (_lastTelegramBotSnapshot is null || _lastTelegramBotScreenshot is null)
            {
                TelegramBotStatusText.Text = "Kein Screenshot vorhanden. Lokaler Browser-Screenshot wird erstellt ...";
                _lastTelegramBotScreenshot = await CaptureBrowserWindowPngAsync();
                _lastTelegramBotSnapshot = CreateLocalTelegramSnapshot("NyxNova Update", "local://nyxnova/update");
                if (TelegramBotChanges.Count == 0)
                {
                    SetTelegramBotLocalChange("NyxNova Update", "Manueller Update-Text wurde vorbereitet.");
                }
            }

            var token = string.IsNullOrWhiteSpace(TelegramBotTokenBox.Password)
                ? _telegramBotService.LoadToken()
                : TelegramBotTokenBox.Password;
            if (string.IsNullOrWhiteSpace(token))
            {
                TelegramBotStatusText.Text = "Telegram Bot Token fehlt.";
                return;
            }

            var caption = BuildTelegramCaptionHtml();
            TelegramBotStatusText.Text = "Telegram Update wird gesendet ...";
            await _telegramBotService.SendPhotoAsync(token, TelegramBotChatIdBox.Text, _lastTelegramBotScreenshot, caption, "HTML");
            TelegramBotStatusText.Text = "Update wurde an Telegram gesendet.";
        }
        catch (Exception ex)
        {
            TelegramBotStatusText.Text = BuildTelegramErrorText(ex);
        }
    }

    private void TelegramBotChange_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TelegramBotChangeListBox.SelectedItem is TelegramBotChange change &&
            !string.IsNullOrWhiteSpace(change.TargetUrl))
        {
            TelegramBotPanel.Visibility = Visibility.Collapsed;
            NavigateActive(change.TargetUrl);
        }
    }

    private void SaveTelegramBotSettings()
    {
        _settingsService.Current.TelegramBotChatId = TelegramBotChatIdBox.Text.Trim();
        _settingsService.Current.TelegramBotEnabled = TelegramBotEnabledCheckBox.IsChecked == true;
        _settingsService.Save();

        if (!string.IsNullOrWhiteSpace(TelegramBotTokenBox.Password))
        {
            if (TelegramBotRememberTokenCheckBox.IsChecked == true)
            {
                _telegramBotService.SaveToken(TelegramBotTokenBox.Password);
                TelegramBotTokenBox.Clear();
            }
            else
            {
                _telegramBotService.DeleteToken();
            }
        }
    }

    private async Task AnalyzeCurrentPageForTelegramAsync(bool saveSnapshot)
    {
        try
        {
            if (_activeTab?.Browser is not { } browser || !AddressParser.IsWebUrl(_activeTab.Url))
            {
                TelegramBotStatusText.Text = "Telegram Bot kann nur echte Webseiten pruefen.";
                return;
            }

            TelegramBotStatusText.Text = "Webseite wird gelesen und Screenshot wird erstellt ...";
            var snapshot = await CaptureTelegramSnapshotAsync(browser);
            var previous = _telegramBotService.GetSnapshot(snapshot.Url);
            var changes = _telegramBotService.Analyze(previous, snapshot);

            TelegramBotChanges.Clear();
            foreach (var change in changes)
            {
                TelegramBotChanges.Add(change);
            }

            _lastTelegramBotSnapshot = snapshot;
            _lastTelegramBotScreenshot = await browser.CaptureScreenshotAsync(CaptureScreenshotFormat.Png);

            if (saveSnapshot)
            {
                _telegramBotService.SaveSnapshot(snapshot);
            }

            TelegramBotSummaryText.Text = BuildTelegramCaption();
            TelegramBotStatusText.Text = "Analyse fertig. Eintrag doppelklicken, um die Seite zu oeffnen.";
        }
        catch (Exception ex)
        {
            TelegramBotStatusText.Text = $"Analyse fehlgeschlagen: {ex.Message}";
        }
    }

    // Nimmt bevorzugt die offene Webseite direkt per CEF auf (zuverlaessig, unabhaengig
    // davon ob das Fenster im Vordergrund ist). Nur bei internen Nova-Seiten (WPF) oder
    // fehlendem Browser wird der Fensterbereich abfotografiert.
    private async Task<byte[]> CaptureActiveViewPngAsync()
    {
        if (_activeTab?.Browser is { } browser && AddressParser.IsWebUrl(_activeTab.Url))
        {
            return await browser.CaptureScreenshotAsync(CaptureScreenshotFormat.Png);
        }

        return await CaptureBrowserWindowPngAsync();
    }

    private async Task<byte[]> CaptureBrowserWindowPngAsync()
    {
        var wasVisible = TelegramBotPanel.Visibility == Visibility.Visible;
        TelegramBotPanel.BeginAnimation(OpacityProperty, null);
        TelegramBotPanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TelegramBotPanel.Visibility = Visibility.Collapsed;
        TelegramBotPanel.Opacity = 0;
        TelegramBotPanelTransform.X = -28;

        // Fenster nach vorne holen, damit die Bildschirmkopie die echten Inhalte und
        // nicht ein verdeckendes Fenster erwischt.
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();

        await Dispatcher.InvokeAsync(UpdateLayout);
        await Task.Delay(180);

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            {
                throw new InvalidOperationException("Fensterbereich konnte nicht ermittelt werden.");
            }

            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);
            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            TelegramBotPanel.Visibility = wasVisible ? Visibility.Visible : Visibility.Collapsed;
            TelegramBotPanel.Opacity = wasVisible ? 1 : 0;
            TelegramBotPanelTransform.X = wasVisible ? 0 : -28;
        }
    }

    private static string BuildTelegramErrorText(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("chat not found", StringComparison.OrdinalIgnoreCase))
        {
            return "Telegram Fehler: Chat/Channel nicht gefunden. Fuer Channels: Bot als Admin hinzufuegen und @channelname oder die -100... Channel-ID eintragen.";
        }

        if (message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return "Telegram Fehler: Bot Token ist ungueltig.";
        }

        if (message.Contains("Bad Request", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("400", StringComparison.OrdinalIgnoreCase))
        {
            return "Telegram Fehler: Telegram lehnt die Anfrage ab. Pruefe Bot Token und Chat ID.";
        }

        return $"Telegram Fehler: {message}";
    }

    private static TelegramBotSnapshot CreateLocalTelegramSnapshot(string title, string source)
    {
        return new TelegramBotSnapshot
        {
            Url = source,
            Title = title,
            Text = $"Lokaler NyxNova Screenshot: {title}",
            TextHash = TelegramBotService.ComputeHash(source + title + DateTime.Now.Ticks),
            CapturedAt = DateTime.Now
        };
    }

    private void SetTelegramBotLocalChange(string title, string detail)
    {
        TelegramBotChanges.Clear();
        TelegramBotChanges.Add(new TelegramBotChange
        {
            Title = title,
            Detail = detail,
            TargetUrl = _activeTab?.Url ?? AddressParser.HomeUrl,
            Icon = "\uE722"
        });

        TelegramBotSummaryText.Text = BuildTelegramCaption();
    }

    private string GetTelegramBotUserText()
    {
        var text = TelegramBotMessageBox.Text.Trim();
        return text == "Schreibe hier, was geaendert wurde..." ? "" : text;
    }

    // Saubere, uebersichtliche Telegram-Nachricht: nur Bereich und Aenderung,
    // kein Dateiname und kein Dateipfad.
    private string BuildTelegramCaption()
    {
        var lines = new List<string>
        {
            "NyxNova Update",
            "",
            $"Bereich: {GetTelegramBotMode()}",
            $"Art: {GetTelegramBotChangeType()}"
        };

        var text = GetTelegramBotUserText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            lines.Add("");
            lines.Add(text);
        }

        return string.Join("\n", lines);
    }

    // HTML-Variante fuer den Versand mit parse_mode=HTML. Dynamische Werte werden
    // escaped, damit Zeichen wie & oder < (z. B. "Toolbar & Adressleiste") Telegram
    // nicht den Parser zerlegen.
    private string BuildTelegramCaptionHtml()
    {
        static string Esc(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");

        var lines = new List<string>
        {
            "\U0001F30C <b>NyxNova Update</b>",
            "",
            $"\U0001F4E6 <b>Bereich:</b> {Esc(GetTelegramBotMode())}",
            $"\U0001F527 <b>Art:</b> {Esc(GetTelegramBotChangeType())}"
        };

        var text = GetTelegramBotUserText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            lines.Add("");
            lines.Add(Esc(text));
        }

        return string.Join("\n", lines);
    }

    private static async Task<TelegramBotSnapshot> CaptureTelegramSnapshotAsync(ChromiumWebBrowser browser)
    {
        const string script = """
(() => JSON.stringify({
  url: location.href,
  title: document.title || location.href,
  text: (document.body ? document.body.innerText : '').replace(/\s+/g, ' ').trim().slice(0, 16000),
  headings: Array.from(document.querySelectorAll('h1,h2,h3')).map(x => x.innerText.trim()).filter(Boolean).slice(0, 40),
  links: Array.from(document.links).map(a => a.href).filter(Boolean).slice(0, 80)
}))()
""";

        var response = await browser.EvaluateScriptAsync(script);
        if (!response.Success || response.Result is not string json)
        {
            throw new InvalidOperationException(response.Message ?? "Seiteninhalt konnte nicht gelesen werden.");
        }

        var data = JsonSerializer.Deserialize<TelegramProbeData>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Seitenanalyse konnte nicht ausgewertet werden.");

        return new TelegramBotSnapshot
        {
            Url = data.Url ?? "",
            Title = data.Title ?? "",
            Text = data.Text ?? "",
            Headings = data.Headings ?? new List<string>(),
            Links = data.Links ?? new List<string>(),
            CapturedAt = DateTime.Now
        };
    }

    private void Downloads_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        NavigateActive("nova://downloads");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        NavigateActive("nova://settings");
    }

    private void ExtensionsMenu_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        NavigateActive("nova://extensions");
    }

    private void OpenStore_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        NavigateActive("nova://store");
    }

    private void BookmarksMenu_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        StatusText.Text = Bookmarks.Count == 0 ? "Noch keine Lesezeichen." : $"{Bookmarks.Count} Lesezeichen gespeichert.";
    }

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        var window = new MainWindow();
        window.Show();
        window.Activate();
        StatusText.Text = "Neues Nova-Fenster geoeffnet.";
    }

    private void Incognito_Click(object sender, RoutedEventArgs e)
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        var window = new MainWindow(true);
        window.Show();
        window.Activate();
        window.Title = "NovaBrowser.CefSharp - Privat";
        StatusText.Text = "Privates Nova-Fenster geoeffnet.";
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ChangeZoom(-0.5);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ChangeZoom(0.5);
    }

    private void ChangeZoom(double delta)
    {
        if (_activeTab?.Browser is null)
        {
            return;
        }

        _settingsService.Current.ZoomLevel = Math.Clamp(_settingsService.Current.ZoomLevel + delta, -3, 3);
        _activeTab.Browser.SetZoomLevel(_settingsService.Current.ZoomLevel);
        MenuPanel.ZoomText = $"{Math.Round(100 * Math.Pow(1.2, _settingsService.Current.ZoomLevel))} %";
        _settingsService.Save();
    }

    private void ResetZoom()
    {
        if (_activeTab?.Browser is null)
        {
            return;
        }

        _settingsService.Current.ZoomLevel = 0;
        _activeTab.Browser.SetZoomLevel(0);
        MenuPanel.ZoomText = "100 %";
        _settingsService.Save();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        _activeTab?.Browser?.Print();
    }

    private void SavePage_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        if (_activeTab is not null && AddressParser.IsWebUrl(_activeTab.Url))
        {
            _activeTab.Browser?.GetBrowserHost()?.StartDownload(_activeTab.Url);
            StatusText.Text = "Seite wird ueber den Download-Manager gespeichert.";
        }
    }

    private void CopyCurrentUrlToClipboard()
    {
        var url = _activeTab?.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusText.Text = "Kein Link zum Kopieren verfuegbar.";
            return;
        }

        Clipboard.SetText(url);
        StatusText.Text = "Link wurde kopiert.";
    }

    private void ShowFindBar()
    {
        CloseTransientPanels();
        if (_activeTab?.Browser is null || !AddressParser.IsWebUrl(_activeTab.Url))
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
            StatusText.Text = "Seitensuche ist fuer Webseiten verfuegbar.";
            return;
        }

        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
        if (!string.IsNullOrWhiteSpace(FindBox.Text))
        {
            FindInPage(forward: true, findNext: false);
        }
    }

    private void CloseFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        _activeTab?.Browser?.GetBrowserHost()?.StopFinding(clearSelection: true);
    }

    private void FindInPage(bool forward, bool findNext)
    {
        var query = FindBox.Text.Trim();
        if (_activeTab?.Browser is null || string.IsNullOrWhiteSpace(query))
        {
            _activeTab?.Browser?.GetBrowserHost()?.StopFinding(clearSelection: true);
            return;
        }

        _activeTab.Browser.GetBrowserHost()?.Find(query, forward, false, findNext);
        StatusText.Text = $"Suche in Seite: {query}";
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FindBar.Visibility != Visibility.Visible)
        {
            return;
        }

        _findRequestId++;
        FindInPage(forward: true, findNext: false);
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindInPage(forward: Keyboard.Modifiers != ModifierKeys.Shift, findNext: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFindBar();
            e.Handled = true;
        }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindInPage(forward: false, findNext: true);
    private void FindNext_Click(object sender, RoutedEventArgs e) => FindInPage(forward: true, findNext: true);
    private void CloseFindBar_Click(object sender, RoutedEventArgs e) => CloseFindBar();

    private void DownloadsPopup_Click(object sender, RoutedEventArgs e)
    {
        ToggleDownloadsPopup();
    }

    private void DownloadButtonShell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ToggleDownloadsPopup();
        e.Handled = true;
    }

    private void ToggleDownloadsPopup()
    {
        if (_downloadsFlyoutWindow is not null)
        {
            _downloadsFlyoutWindow.Close();
            _downloadsFlyoutWindow = null;
            StatusText.Text = "Downloads geschlossen.";
            return;
        }

        ShowDownloadsFlyout();
    }

    private void ShowDownloadsFlyout()
    {
        CloseTransientPanels();
        OnPropertyChanged(nameof(RecentDownloads));

        var content = CreateFlyoutPanel(390);
        var stack = new StackPanel();
        content.Child = stack;
        stack.Children.Add(new TextBlock { Text = "Downloads", Foreground = FindBrush("NovaText"), FontSize = 19, FontWeight = FontWeights.Black });

        if (!Downloads.Any())
        {
            stack.Children.Add(new TextBlock { Text = "Noch keine Downloads.", Foreground = FindBrush("MutedText"), Margin = new Thickness(0, 6, 0, 12) });
        }
        else
        {
            foreach (var item in Downloads.Take(5))
            {
                stack.Children.Add(CreateDownloadFlyoutRow(item));
            }
        }

        _downloadsFlyoutWindow = ShowFlyoutWindow(content, DownloadsButton, 390);
        _downloadsFlyoutWindow.Closed += (_, _) => _downloadsFlyoutWindow = null;
        StatusText.Text = "Downloads geoeffnet.";
    }

    private void AddonDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem item })
        {
            CloseTransientPanels();
            NavigateActive($"nova://store/addon/{item.Id}");
        }
    }

    private void ShowAddonActionPopup(AddonItem item)
    {
        CloseTransientPanels(ExtensionActionPopup);
        _selectedAddon = item;
        ExtensionActionPopup.DataContext = item;
        ExtensionActionTitle.Text = item.Name;
        ExtensionPinMenuButton.Content = item.PinMenuText;
        ExtensionEnableMenuButton.Content = item.EnabledMenuText;
        ExtensionActionPopup.Visibility = Visibility.Visible;
    }

    private void ExtensionHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAddon is not null)
        {
            CloseTransientPanels();
            NavigateActive($"nova://store/addon/{_selectedAddon.Id}");
        }
    }

    private void InstallAddon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem addon })
        {
            InstallAddon(addon);
        }
    }

    private void InstallZipAddon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Nova Addon ZIP auswaehlen",
            Filter = "Nova Addon ZIP (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var preview = _addonService.ReadZipManifest(dialog.FileName);
            var permissionDialog = new AddonPermissionDialog(preview) { Owner = this };
            if (permissionDialog.ShowDialog() != true)
            {
                StatusText.Text = "ZIP-Addon Installation abgebrochen.";
                return;
            }

            var installed = _addonService.InstallFromZip(dialog.FileName, preview);
            NotifyCollectionViews();
            RefreshExtensionsPopup();
            RefreshStoreList();
            RefreshInstalledAddonList();
            StatusText.Text = $"{installed.Name} aus ZIP installiert und aktiviert.";
        }
        catch (Exception ex)
        {
            App.LogException("install-zip-addon", ex);
            MessageBox.Show(this, ex.Message, "Nova Addon konnte nicht installiert werden", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "ZIP-Addon wurde abgelehnt.";
        }
    }

    private void InstallAddon(AddonItem addon)
    {
        if (addon.Installed)
        {
            StatusText.Text = $"{addon.Name} ist bereits installiert.";
            return;
        }

        var dialog = new AddonPermissionDialog(addon) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            StatusText.Text = "Installation abgebrochen.";
            return;
        }

        _addonService.Install(addon.Id);
        NotifyCollectionViews();
        RefreshStoreList();
        RefreshInstalledAddonList();
        StatusText.Text = $"{addon.Name} installiert.";
    }

    private void ManageAddon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem addon })
        {
            CloseTransientPanels();
            NavigateActive(addon.Installed ? "nova://extensions" : $"nova://store/addon/{addon.Id}");
        }
    }

    private void ToggleAddon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem addon })
        {
            _addonService.ToggleEnabled(addon);
            NotifyCollectionViews();
            RefreshExtensionsPopup();
            RefreshInstalledAddonList();
            StatusText.Text = addon.Enabled ? $"{addon.Name} aktiviert." : $"{addon.Name} deaktiviert.";
        }
    }

    private void PinAddon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem addon })
        {
            _addonService.TogglePinned(addon);
            NotifyCollectionViews();
            RefreshExtensionsPopup();
            RefreshInstalledAddonList();
            StatusText.Text = addon.Pinned ? $"{addon.Name} angeheftet." : $"{addon.Name} geloest.";
        }
    }

    private void RemoveAddon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem addon })
        {
            _addonService.Remove(addon);
            NotifyCollectionViews();
            RefreshExtensionsPopup();
            RefreshStoreList();
            RefreshInstalledAddonList();
            ExtensionActionPopup.Visibility = Visibility.Collapsed;
            StatusText.Text = $"{addon.Name} entfernt.";
        }
    }

    private void AddonCardMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AddonItem addon })
        {
            ShowAddonActionPopup(addon);
        }
    }

    private void AddonOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAddon is null)
        {
            return;
        }

        CloseTransientPanels();
        NavigateActive($"nova://store/addon/{_selectedAddon.Id}");
        StatusText.Text = $"Optionen fuer {_selectedAddon.Name} geoeffnet.";
    }

    private void PinSelectedAddon_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAddon is null)
        {
            return;
        }

        _addonService.TogglePinned(_selectedAddon);
        NotifyCollectionViews();
        RefreshExtensionsPopup();
        RefreshInstalledAddonList();
        ExtensionPinMenuButton.Content = _selectedAddon.PinMenuText;
        StatusText.Text = _selectedAddon.Pinned ? $"{_selectedAddon.Name} angeheftet." : $"{_selectedAddon.Name} geloest.";
    }

    private void ToggleSelectedAddon_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAddon is null)
        {
            return;
        }

        _addonService.ToggleEnabled(_selectedAddon);
        NotifyCollectionViews();
        RefreshExtensionsPopup();
        RefreshInstalledAddonList();
        ExtensionEnableMenuButton.Content = _selectedAddon.EnabledMenuText;
        StatusText.Text = _selectedAddon.Enabled ? $"{_selectedAddon.Name} aktiviert." : $"{_selectedAddon.Name} deaktiviert.";
    }

    private void RemoveSelectedAddon_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAddon is null)
        {
            return;
        }

        var name = _selectedAddon.Name;
        _addonService.Remove(_selectedAddon);
        _selectedAddon = null;
        NotifyCollectionViews();
        RefreshExtensionsPopup();
        RefreshStoreList();
        RefreshInstalledAddonList();
        ExtensionActionPopup.Visibility = Visibility.Collapsed;
        StatusText.Text = $"{name} entfernt.";
    }

    private void AddonDetailInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDetailAddon is null)
        {
            return;
        }

        if (_activeDetailAddon.Installed)
        {
            _addonService.Remove(_activeDetailAddon);
            StatusText.Text = $"{_activeDetailAddon.Name} entfernt.";
        }
        else
        {
            InstallAddon(_activeDetailAddon);
        }

        ShowAddonDetail(_activeDetailAddon.Id);
    }

    private void MoreTools_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        StatusText.Text = "Mehr Tools: Drucken, Seite speichern, Downloads und Erweiterungen sind verfuegbar.";
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.Current.SearchEngine = SearchEngine.Google;
        _settingsService.Current.ShowBookmarkBar = false;
        _settingsService.Current.ZoomLevel = 0;
        _settingsService.Current.TrackerBlockerEnabled = true;
        _settingsService.Current.AggressiveTrackerBlockerEnabled = false;
        _settingsService.Current.HttpsOnlyModeEnabled = false;
        _settingsService.Current.TabSleepEnabled = true;
        _settingsService.Current.HardwareAccelerationEnabled = true;
        _settingsService.Current.EcoModeEnabled = false;
        _settingsService.Current.SmartSessionRestoreEnabled = true;
        _settingsService.Current.GlobalFingerprintingProtectionEnabled = true;
        _settingsService.Current.AutomaticProtectionEnabled = true;
        _settingsService.Current.DarkModeEnforcerEnabled = false;
        _settingsService.Current.LazyMediaLoadingEnabled = true;
        SearchEngineBox.SelectedIndex = 0;
        SettingsSearchEngineBox.SelectedIndex = 0;
        BookmarkBarCheckBox.IsChecked = false;
        TrackerBlockerCheckBox.IsChecked = true;
        AggressiveTrackerBlockerCheckBox.IsChecked = false;
        HttpsOnlyModeCheckBox.IsChecked = false;
        TabSleepCheckBox.IsChecked = true;
        HardwareAccelerationCheckBox.IsChecked = true;
        EcoModeCheckBox.IsChecked = false;
        SmartSessionRestoreCheckBox.IsChecked = true;
        GlobalFingerprintingCheckBox.IsChecked = true;
        AutomaticProtectionCheckBox.IsChecked = true;
        DarkModeEnforcerCheckBox.IsChecked = false;
        LazyMediaLoadingCheckBox.IsChecked = true;
        UpdateBookmarkBarVisibility();
        _settingsService.Save();
        StatusText.Text = "Einstellungen zurueckgesetzt.";
    }

    private void ClearBrowserData_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        _historyService.Clear();
        _downloadService.Clear();
        Cef.GetGlobalCookieManager()?.DeleteCookies("", "");
        NotifyCollectionViews();
        StatusText.Text = "Browserdaten wurden geloescht: Verlauf, Downloads und Cookies.";
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        CloseTransientPanels();
        StatusText.Text = "Hilfe: Nova interne Seiten nutzen nova://start, nova://store, nova://extensions, nova://settings.";
    }

    private void SettingsNavDesign_Click(object sender, MouseButtonEventArgs e) => NavigateActive("nova://settings/design");
    private void SettingsNavSearch_Click(object sender, MouseButtonEventArgs e) => NavigateActive("nova://settings/search");
    private void SettingsNavBookmarks_Click(object sender, MouseButtonEventArgs e) => NavigateActive("nova://settings/bookmarks");
    private void SettingsNavDownloads_Click(object sender, MouseButtonEventArgs e) => NavigateActive("nova://settings/downloads");
    private void SettingsNavAddons_Click(object sender, MouseButtonEventArgs e) => NavigateActive("nova://settings/addons");
    private void SettingsNavPrivacy_Click(object sender, MouseButtonEventArgs e) => NavigateActive("nova://settings/privacy");
    private void SettingsNavBuild_Click(object sender, MouseButtonEventArgs e) => NavigateActive("nova://settings/build");

    private void ShowSettingsCategory(string category)
    {
        if (SettingsDesignSection is null)
        {
            return;
        }

        HideAllSettingsSections();
        ResetSettingsNavState();

        switch (category)
        {
            case "search":
                SettingsPageTitle.Text = "SUCHE UND ADRESSE";
                SettingsPageSubtitle.Text = "Suchmaschine, Adressleiste und Eingabe-Verhalten.";
                SettingsSearchSection.Visibility = Visibility.Visible;
                SetSettingsNavActive(SettingsNavSearch);
                SettingsSearchSection.BringIntoView();
                break;
            case "bookmarks":
                SettingsPageTitle.Text = "LESEZEICHEN";
                SettingsPageSubtitle.Text = "Startseiten-Verhalten und Lesezeichenleiste.";
                SettingsStartSection.Visibility = Visibility.Visible;
                SetSettingsNavActive(SettingsNavBookmarks);
                SettingsStartSection.BringIntoView();
                break;
            case "downloads":
                SettingsPageTitle.Text = "DOWNLOADS";
                SettingsPageSubtitle.Text = "Download-Ort, Download-Popup und Download-Verlauf.";
                SettingsDownloadsSection.Visibility = Visibility.Visible;
                SetSettingsNavActive(SettingsNavDownloads);
                SettingsDownloadsSection.BringIntoView();
                break;
            case "addons":
                SettingsPageTitle.Text = "ADDONS UND STORE";
                SettingsPageSubtitle.Text = "Nova Addons, Store, ZIP-Import und Erweiterungsrechte.";
                SettingsAddonsSection.Visibility = Visibility.Visible;
                SetSettingsNavActive(SettingsNavAddons);
                SettingsAddonsSection.BringIntoView();
                break;
            case "privacy":
                SettingsPageTitle.Text = "DATENSCHUTZ UND MEDIEN";
                SettingsPageSubtitle.Text = "Cookies, Cache, Medien-Diagnose und Webseiten-Berechtigungen.";
                SettingsPrivacySection.Visibility = Visibility.Visible;
                SetSettingsNavActive(SettingsNavPrivacy);
                SettingsPrivacySection.BringIntoView();
                break;
            case "build":
                SettingsPageTitle.Text = "UPDATE UND BUILD";
                SettingsPageSubtitle.Text = "Version, Build-Zeit, Beta-Update und Zuruecksetzen.";
                SettingsBuildSection.Visibility = Visibility.Visible;
                SetSettingsNavActive(SettingsNavBuild);
                SettingsBuildSection.BringIntoView();
                break;
            default:
                SettingsPageTitle.Text = "DESIGN UND STARTSEITE";
                SettingsPageSubtitle.Text = "Aussehen, Startseite und Nova-Oberflaeche.";
                SettingsDesignSection.Visibility = Visibility.Visible;
                SettingsStartSection.Visibility = Visibility.Visible;
                SetSettingsNavActive(SettingsNavDesign);
                SettingsDesignSection.BringIntoView();
                break;
        }
    }

    private void HideAllSettingsSections()
    {
        SettingsDesignSection.Visibility = Visibility.Collapsed;
        SettingsSearchSection.Visibility = Visibility.Collapsed;
        SettingsStartSection.Visibility = Visibility.Collapsed;
        SettingsDownloadsSection.Visibility = Visibility.Collapsed;
        SettingsAddonsSection.Visibility = Visibility.Collapsed;
        SettingsPrivacySection.Visibility = Visibility.Collapsed;
        SettingsBuildSection.Visibility = Visibility.Collapsed;
    }

    private void ResetSettingsNavState()
    {
        foreach (var nav in new[] { SettingsNavDesign, SettingsNavSearch, SettingsNavBookmarks, SettingsNavDownloads, SettingsNavAddons, SettingsNavPrivacy, SettingsNavBuild })
        {
            nav.Background = new SolidColorBrush(Color.FromRgb(18, 13, 25));
        }
    }

    private static void SetSettingsNavActive(Border nav)
    {
        nav.Background = new SolidColorBrush(Color.FromRgb(37, 23, 56));
    }

    private void ThemeCardNovaNeon_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("NovaNeon");
    private void ThemeCardAurora_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Aurora");
    private void ThemeCardFokus_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Fokus");
    private void ThemeCardGlas_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Glas");

    private void ApplyTheme(string theme)
    {
        NovaThemeService.Apply(theme, Resources);
        _settingsService.Current.Theme = theme;
        _settingsService.Save();
        UpdateThemeCardStatus(theme);
    }

    private void UpdateThemeCardStatus(string theme)
    {
        ThemeStatusNovaNeon.Text = theme == "NovaNeon" ? "Aktiv" : "Neon";
        ThemeStatusAurora.Text = theme == "Aurora" ? "Aktiv" : "Eisblau";
        ThemeStatusFokus.Text = theme == "Fokus" ? "Aktiv" : "Ruhig";
        ThemeStatusGlas.Text = theme == "Glas" ? "Aktiv" : "Panels";
    }

    private void SettingsSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SettingsSearchBox.Text == "Einstellungen durchsuchen")
        {
            SettingsSearchBox.Clear();
        }
    }

    private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        FilterSettingsSections(SettingsSearchBox.Text);
    }

    private void FilterSettingsSections(string query)
    {
        query = (query ?? "").Trim();
        if (string.IsNullOrEmpty(query) || query == "Einstellungen durchsuchen")
        {
            ShowSettingsCategory("design");
            return;
        }

        SettingsPageTitle.Text = "SUCHERGEBNISSE";
        SettingsPageSubtitle.Text = "Passende Einstellungsbereiche aus NyxNova.";
        ResetSettingsNavState();

        bool Matches(string keywords) => keywords.Contains(query, StringComparison.OrdinalIgnoreCase);

        SettingsDesignSection.Visibility = Matches("Design Theme Nova Neon Aurora Fokus Glas Hintergrund") ? Visibility.Visible : Visibility.Collapsed;
        SettingsSearchSection.Visibility = Matches("Suche Suchmaschine Adressleiste Google DuckDuckGo Bing") ? Visibility.Visible : Visibility.Collapsed;
        SettingsStartSection.Visibility = Matches("Start und Oberflaeche Startseite Lesezeichenleiste") ? Visibility.Visible : Visibility.Collapsed;
        SettingsDownloadsSection.Visibility = Matches("Downloads Download Datei Ordner Verlauf Fortschritt") ? Visibility.Visible : Visibility.Collapsed;
        SettingsAddonsSection.Visibility = Matches("Nova Addons Erweiterungen Store gepinnte Quick Links") ? Visibility.Visible : Visibility.Collapsed;
        SettingsPrivacySection.Visibility = Matches("Datenschutz und Medien Cookies Cache LocalStorage WebSecurity Diagnose Tracker Blocker HTTPS only Tab Ruhestand Hardware Beschleunigung Eco Mode Smart Session Restore Fingerprinting Automatischer Schutz") ? Visibility.Visible : Visibility.Collapsed;
        SettingsBuildSection.Visibility = Matches("Build und Beta Update Version") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddonSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (AddonSearchBox.Text == "Addons suchen")
        {
            AddonSearchBox.Clear();
        }
    }

    private void AddonSearchBox_KeyUp(object sender, KeyEventArgs e) => RefreshInstalledAddonList();
    private void AddonFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshInstalledAddonList();
        }
    }

    private void StoreSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (StoreSearchBox.Text == "NovaStore durchsuchen")
        {
            StoreSearchBox.Clear();
        }
    }

    private void StoreSearchBox_KeyUp(object sender, KeyEventArgs e) => RefreshStoreList();
    private void StoreCategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshStoreList();
        }
    }

    private void StoreChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string category })
        {
            for (var i = 0; i < StoreCategoryBox.Items.Count; i++)
            {
                if (StoreCategoryBox.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Content?.ToString(), category, StringComparison.OrdinalIgnoreCase))
                {
                    StoreCategoryBox.SelectedIndex = i;
                    break;
                }
            }

            RefreshStoreList();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void CloseGoogleNotice_Click(object sender, RoutedEventArgs e)
    {
        GoogleNotice.Visibility = Visibility.Collapsed;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
            e.Key == Key.Delete)
        {
            ClearBrowserData_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                 e.Key == Key.N)
        {
            Incognito_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.N)
        {
            NewWindow_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.T)
        {
            OpenInNewTab(AddressParser.HomeUrl);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.W)
        {
            CloseActiveTab();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                 e.Key == Key.Tab)
        {
            SelectRelativeTab(-1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Tab)
        {
            SelectRelativeTab(1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.PageUp)
        {
            SelectRelativeTab(-1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.PageDown)
        {
            SelectRelativeTab(1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.L)
        {
            FocusAddressBar();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (e.Key == Key.E || e.Key == Key.K))
        {
            FocusAddressBar();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.F)
        {
            ShowFindBar();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D)
        {
            Bookmark_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.J)
        {
            Downloads_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.H)
        {
            History_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                 e.Key == Key.R)
        {
            ReloadWebTab(ignoreCache: true);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.R)
        {
            Reload_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Reload_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.S)
        {
            SavePage_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.P)
        {
            Print_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.OemComma)
        {
            Settings_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.OemPlus)
        {
            ZoomIn_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.OemMinus)
        {
            ZoomOut_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D0)
        {
            ResetZoom();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == Key.Left)
        {
            Back_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == Key.Right)
        {
            Forward_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key == Key.Home)
        {
            Home_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            SelectTabByShortcutIndex(e.Key - Key.D0);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
                 e.Key == Key.I)
        {
            OpenDevTools_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F12)
        {
            OpenDevTools_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseTransientPanels();
            if (FindBar.Visibility == Visibility.Visible)
            {
                CloseFindBar();
            }

            if (_isFullscreen)
            {
                ToggleFullscreen();
            }
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            IsInsideElement(source, NewTabButton) ||
            IsInsideElement(source, WindowButtonsPanel) ||
            IsInsideType<Button>(source))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse state changed during a click; keeping the window responsive matters more.
        }
    }

    private void WindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch
        {
            // DragMove can throw if the click turns into another mouse operation.
        }
    }

    private async void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_windowStateTransitioning)
        {
            return;
        }

        _windowStateTransitioning = true;
        AnimateWindowChromeTransition(minimize: true);
        await Task.Delay(95);
        WindowState = WindowState.Minimized;
        _windowStateTransitioning = false;
    }

    private async void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_windowStateTransitioning)
        {
            return;
        }

        _windowStateTransitioning = true;
        AnimateWindowChromeTransition(minimize: false);
        await Task.Delay(70);
        ToggleMaximized();
        _windowStateTransitioning = false;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }

        UpdateWindowChromeState();
    }

    private void ToggleMaximized()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowChromeState();
    }

    private void AnimateWindowChromeTransition(bool minimize)
    {
        try
        {
            var duration = TimeSpan.FromMilliseconds(minimize ? 90 : 120);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (WindowFrame.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                WindowFrame.RenderTransform = scale;
            }

            var scaleTo = minimize ? 0.985 : 0.994;
            var scaleAnimation = new DoubleAnimation
            {
                To = scaleTo,
                Duration = duration,
                AutoReverse = !minimize,
                EasingFunction = ease,
                FillBehavior = minimize ? FillBehavior.HoldEnd : FillBehavior.Stop
            };

            var opacityAnimation = new DoubleAnimation
            {
                To = minimize ? 0.72 : 0.94,
                Duration = duration,
                AutoReverse = !minimize,
                EasingFunction = ease,
                FillBehavior = minimize ? FillBehavior.HoldEnd : FillBehavior.Stop
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            BeginAnimation(OpacityProperty, opacityAnimation);

            if (!minimize)
            {
                Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(150);
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    BeginAnimation(OpacityProperty, null);
                    scale.ScaleX = 1;
                    scale.ScaleY = 1;
                    Opacity = 1;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            App.LogException("window-transition-animation", ex);
        }
    }

    private void ToggleFullscreen()
    {
        SetFullscreen(!_isFullscreen);
    }

    private void SetFullscreenFromWebContent(bool fullscreen)
    {
        SetFullscreen(fullscreen);
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (fullscreen == _isFullscreen)
        {
            return;
        }

        if (fullscreen)
        {
            CloseTransientPanels();
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.Maximized;
            TitleBar.Visibility = Visibility.Collapsed;
            ToolbarBar.Visibility = Visibility.Collapsed;
            BookmarkBar.Visibility = Visibility.Collapsed;
            SidebarPlaceholder.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
            ToolbarRow.Height = new GridLength(0);
            BookmarkRow.Height = new GridLength(0);
            StatusRow.Height = new GridLength(0);
            SidebarColumn.Width = new GridLength(0);
            WindowFrame.BorderThickness = new Thickness(0);
            Topmost = true;
            _isFullscreen = true;
        }
        else
        {
            Topmost = false;
            WindowState = _stateBeforeFullscreen;
            TitleBar.Visibility = Visibility.Visible;
            ToolbarBar.Visibility = Visibility.Visible;
            BookmarkBar.Visibility = Visibility.Collapsed;
            SidebarPlaceholder.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(40);
            ToolbarRow.Height = new GridLength(42);
            BookmarkRow.Height = new GridLength(0);
            StatusRow.Height = new GridLength(0);
            SidebarColumn.Width = new GridLength(48);
            WindowFrame.BorderThickness = new Thickness(0);
            _isFullscreen = false;
        }

        UpdateWindowChromeState();
    }

    private void UpdateWindowChromeState()
    {
        if (_isFullscreen)
        {
            WindowFrame.CornerRadius = new CornerRadius(0);
            WindowFrame.BorderThickness = new Thickness(0);
            MaximizeButton.Content = "\uE923";
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            WindowFrame.CornerRadius = new CornerRadius(0);
            WindowFrame.BorderThickness = new Thickness(0);
            MaximizeButton.Content = "\uE923";
        }
        else
        {
            WindowFrame.CornerRadius = new CornerRadius(12);
            WindowFrame.BorderThickness = new Thickness(0);
            MaximizeButton.Content = "\uE922";
        }
    }

    private void RootGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (source is null)
        {
            return;
        }

        // Klicks in einem offenen ComboBox-Dropdown landen in einem separaten Popup-Fenster,
        // das nicht im Visualbaum des Panels liegt. Ohne diese Pruefung wuerde die Auswahl
        // eines Dropdown-Eintrags als "Klick ausserhalb" gewertet und das Panel schliessen.
        if (IsInsideType<System.Windows.Controls.ComboBoxItem>(source) ||
            IsAnyPanelComboBoxDropDownOpen())
        {
            return;
        }

        if (IsInsideAnyPopup(source) ||
            IsInsideElement(source, ExtensionsButton) ||
            IsInsideElement(source, BookmarkButton) ||
            IsInsideElement(source, BookmarkButtonShell) ||
            IsInsideElement(source, DownloadsButton) ||
            IsInsideElement(source, DownloadButtonShell) ||
            IsInsideElement(source, AddressBarShell) ||
            IsInsideElement(source, TelegramBotPanel) ||
            IsInsideElement(source, TelegramBotButton) ||
            IsInsideElement(source, MainMenuButton) ||
            IsInsideElement(source, NewTabButton))
        {
            return;
        }

        CloseTransientPanels();
    }

    private bool IsAnyPanelComboBoxDropDownOpen()
    {
        return TelegramBotModeBox?.IsDropDownOpen == true ||
               TelegramBotChangeTypeBox?.IsDropDownOpen == true;
    }

    private void CloseTransientPanels(FrameworkElement? except = null)
    {
        if (!ReferenceEquals(except, DownloadsPopup))
        {
            CloseDownloadsFlyout();
        }

        if (!ReferenceEquals(except, BookmarkPopup))
        {
            CloseBookmarkFlyout();
        }

        foreach (var panel in new FrameworkElement[]
                 {
                     MenuPanel,
                     ExtensionsPopup,
                     ExtensionActionPopup,
                     BookmarkPopup,
                     HistorySidePanel,
                     TelegramBotPanel,
                     DownloadsPopup,
                     OmniboxPopup
                 })
        {
            if (!ReferenceEquals(panel, except))
            {
                panel.Visibility = Visibility.Collapsed;
                if (ReferenceEquals(panel, DownloadsPopup))
                {
                    DownloadsPopupHost.IsOpen = false;
                }
                else if (ReferenceEquals(panel, OmniboxPopup))
                {
                    OmniboxPopupHost.IsOpen = false;
                    OmniboxSuggestions.Clear();
                }
            }
        }
    }

    private void CloseDownloadsFlyout()
    {
        var window = _downloadsFlyoutWindow;
        _downloadsFlyoutWindow = null;
        window?.Close();
    }

    private void CloseBookmarkFlyout()
    {
        var window = _bookmarkFlyoutWindow;
        _bookmarkFlyoutWindow = null;
        window?.Close();
    }

    private Border CreateFlyoutPanel(double width)
    {
        return new Border
        {
            Width = width,
            MaxHeight = 540,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(18),
            Background = FindBrush("NovaGlassPanel"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(185, 103, 255)),
            BorderThickness = new Thickness(1.2),
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 0,
                Color = Color.FromRgb(159, 77, 255),
                Opacity = 0.42
            }
        };
    }

    private Window ShowFlyoutWindow(FrameworkElement content, FrameworkElement target, double width)
    {
        var point = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight + 8));
        var workArea = SystemParameters.WorkArea;
        var left = Math.Clamp(point.X - width, workArea.Left + 8, workArea.Right - width - 8);
        var top = Math.Clamp(point.Y, workArea.Top + 8, workArea.Bottom - 120);

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = left,
            Top = top,
            Content = content,
            Topmost = true
        };
        window.Show();
        return window;
    }

    private Button CreateFlyoutActionButton(string text)
    {
        return new Button
        {
            Content = text,
            Height = 34,
            MinWidth = 86,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0),
            Background = FindBrush("NovaButton"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(197, 140, 255)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
    }

    private Border CreateDownloadFlyoutRow(NovaDownloadItem item)
    {
        var openButton = CreateFlyoutIconButton("\uE8A7", "Oeffnen");
        openButton.Click += (_, _) => DownloadService.Open(item);
        var folderButton = CreateFlyoutIconButton("\uE8DA", "Im Ordner anzeigen");
        folderButton.Click += (_, _) => DownloadService.ShowInFolder(item);
        var removeButton = CreateFlyoutIconButton("\uE711", "Aus Liste entfernen");
        removeButton.Click += (_, _) =>
        {
            _downloadService.Remove(item);
            CloseDownloadsFlyout();
            ShowDownloadsFlyout();
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(openButton);
        actions.Children.Add(folderButton);
        actions.Children.Add(removeButton);

        var progress = new ProgressBar
        {
            Value = item.Progress,
            Maximum = 100,
            Height = 8,
            Width = 210,
            Margin = new Thickness(0, 7, 0, 0),
            Style = TryFindResource("NovaDownloadProgressBar") as Style
        };

        var textStack = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        textStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = FindBrush("NovaText"), FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 210 });
        textStack.Children.Add(new TextBlock { Text = item.Status, Foreground = FindBrush("MutedText"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        textStack.Children.Add(progress);

        var dock = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        dock.Children.Add(actions);
        dock.Children.Add(textStack);

        return new Border
        {
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Color.FromRgb(34, 21, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(79, 47, 117)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = dock
        };
    }

    private Button CreateFlyoutIconButton(string glyph, string tooltip)
    {
        return new Button
        {
            Content = glyph,
            Width = 30,
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = new SolidColorBrush(Color.FromRgb(201, 140, 255)),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            ToolTip = tooltip,
            Cursor = Cursors.Hand
        };
    }

    private bool ShowBrowserPermissionPrompt(string requestingOrigin, string title, string message)
    {
        var allow = false;
        var dialog = new Window
        {
            Title = "Nova Berechtigung",
            Width = 430,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Owner = this
        };

        var point = AddressBox.PointToScreen(new Point(12, AddressBox.ActualHeight + 14));
        dialog.Left = Math.Max(8, Math.Min(point.X, SystemParameters.WorkArea.Right - dialog.Width - 8));
        dialog.Top = Math.Max(8, point.Y);

        var panel = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromRgb(25, 22, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(181, 96, 255)),
            BorderThickness = new Thickness(1.2),
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 0,
                Color = Color.FromRgb(148, 62, 255),
                Opacity = 0.45
            }
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Margin = new Thickness(0, 2, 12, 0),
            Background = new LinearGradientBrush(Color.FromRgb(113, 52, 196), Color.FromRgb(41, 200, 255), 35)
        };
        icon.Child = new TextBlock
        {
            Text = title.Contains("Mikrofon", StringComparison.OrdinalIgnoreCase) ? "\uE720" :
                   title.Contains("Kamera", StringComparison.OrdinalIgnoreCase) ? "\uE714" :
                   "\uE7F4",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = Brushes.White,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        root.Children.Add(icon);

        var content = new StackPanel();
        Grid.SetColumn(content, 1);
        root.Children.Add(content);

        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Black,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = requestingOrigin,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(191, 169, 218)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(233, 225, 246)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var deny = CreatePermissionPromptButton("Blockieren", false);
        deny.Click += (_, _) =>
        {
            allow = false;
            dialog.Close();
        };

        var accept = CreatePermissionPromptButton("Zulassen", true);
        accept.Margin = new Thickness(10, 0, 0, 0);
        accept.Click += (_, _) =>
        {
            allow = true;
            dialog.Close();
        };

        buttons.Children.Add(deny);
        buttons.Children.Add(accept);
        content.Children.Add(buttons);
        panel.Child = root;
        dialog.Content = panel;
        dialog.ShowDialog();
        return allow;
    }

    private static Button CreatePermissionPromptButton(string text, bool accent)
    {
        return new Button
        {
            Content = text,
            MinWidth = 100,
            Height = 34,
            Padding = new Thickness(14, 0, 14, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(accent ? Color.FromRgb(199, 128, 255) : Color.FromRgb(88, 73, 108)),
            Background = new SolidColorBrush(accent ? Color.FromRgb(141, 54, 236) : Color.FromRgb(39, 34, 50)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand
        };
    }

    private Brush FindBrush(string resourceKey)
    {
        return TryFindResource(resourceKey) as Brush ?? Brushes.White;
    }

    private bool IsInsideAnyPopup(DependencyObject source)
    {
        return IsInsideElement(source, MenuPanel) ||
               IsInsideElement(source, ExtensionsPopup) ||
               IsInsideElement(source, ExtensionActionPopup) ||
               IsInsideElement(source, BookmarkPopup) ||
               IsInsideElement(source, DownloadsPopup);
    }

    private void RefreshExtensionsPopup()
    {
        ExtensionsPopupAddonsList?.Items.Refresh();
        OnPropertyChanged(nameof(PinnedExtensions));
    }

    private void UpdateExtensionAccessState()
    {
        var domain = GetActiveExtensionDomain();
        var siteSettings = GetAddonSiteAccessSettings();
        ExtensionsAllowedForCurrentSite = !siteSettings.TryGetValue(domain, out var value) ||
                                          !value.Equals("false", StringComparison.OrdinalIgnoreCase);

        CurrentExtensionAccessText = "Addons auf dieser Seite erlauben";
        CurrentExtensionAccessHint = $"Aktuelle Seite: {domain}";

        OnPropertyChanged(nameof(CurrentExtensionAccessText));
        OnPropertyChanged(nameof(CurrentExtensionAccessHint));
        OnPropertyChanged(nameof(ExtensionAccessToggleBrush));
        OnPropertyChanged(nameof(ExtensionAccessKnobAlignment));
    }

    private Dictionary<string, string> GetAddonSiteAccessSettings()
    {
        if (!_addonService.Settings.TryGetValue(AddonSiteAccessSettingsKey, out var siteSettings))
        {
            siteSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _addonService.Settings[AddonSiteAccessSettingsKey] = siteSettings;
        }

        return siteSettings;
    }

    private string GetActiveExtensionDomain()
    {
        var url = _activeTab?.Url ?? AddressBox.Text;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return "dieser Seite";
    }

    private static bool IsInsideElement(DependencyObject source, DependencyObject target)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsInsideType<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void RefreshInstalledAddonList()
    {
        var query = AddonSearchBox?.Text?.Trim() ?? "";
        if (query == "Addons suchen")
        {
            query = "";
        }

        var filter = (AddonFilterBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Alle";
        IEnumerable<AddonItem> items = InstalledAddons;
        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(addon => addon.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                         addon.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        items = filter switch
        {
            "Aktiviert" => items.Where(addon => addon.Enabled),
            "Deaktiviert" => items.Where(addon => !addon.Enabled),
            "Angeheftet" => items.Where(addon => addon.Pinned),
            _ => items
        };

        InstalledAddonsList.ItemsSource = items.ToList();
    }

    private void RefreshStoreList()
    {
        var query = StoreSearchBox?.Text?.Trim() ?? "";
        if (query == "NovaStore durchsuchen")
        {
            query = "";
        }

        var category = (StoreCategoryBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Alle";
        IEnumerable<AddonItem> items = StoreAddons;
        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(addon => addon.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                         addon.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                         addon.Author.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (category != "Alle")
        {
            items = items.Where(addon => addon.Category == category);
        }

        StoreAddonsList.ItemsSource = items.ToList();
    }

    private void ShowAddonDetail(string addonId)
    {
        _activeDetailAddon = _addonService.Find(addonId);
        if (_activeDetailAddon is null)
        {
            AddonDetailEyebrow.Text = "NICHT GEFUNDEN";
            AddonDetailName.Text = "Addon nicht gefunden";
            AddonDetailDescription.Text = "Dieses Addon existiert nicht im NovaStore.";
            AddonDetailInstallButton.Visibility = Visibility.Collapsed;
            AddonDetailRatingChip.Text = "★ -";
            AddonDetailStatusChip.Text = "Unbekannt";
            AddonDetailVersion.Text = "-";
            AddonDetailAuthor.Text = "-";
            AddonDetailCategory.Text = "-";
            AddonDetailRating.Text = "-";
            AddonDetailScreenshots.ItemsSource = null;
            AddonDetailPermissions.ItemsSource = null;
            AddonDetailHosts.ItemsSource = null;
            AddonDetailChangelog.Text = "";
            return;
        }

        AddonDetailInstallButton.Visibility = Visibility.Visible;
        AddonDetailIcon.Text = _activeDetailAddon.Icon;
        AddonDetailEyebrow.Text = $"NOVA ADDON · {_activeDetailAddon.Category.ToUpperInvariant()}";
        AddonDetailName.Text = _activeDetailAddon.Name;
        AddonDetailDescription.Text = _activeDetailAddon.Description;
        AddonDetailInstallButton.Content = _activeDetailAddon.Installed ? "Entfernen" : "Installieren";

        AddonDetailRatingChip.Text = $"★ {_activeDetailAddon.Rating:0.0}";
        AddonDetailStatusChip.Text = _activeDetailAddon.Installed ? "Installiert" : "Verifiziert";
        AddonDetailVersion.Text = _activeDetailAddon.Version;
        AddonDetailAuthor.Text = _activeDetailAddon.Author;
        AddonDetailCategory.Text = _activeDetailAddon.Category;
        AddonDetailRating.Text = $"{_activeDetailAddon.Rating:0.0} / 5";

        AddonDetailScreenshots.ItemsSource = _activeDetailAddon.Screenshots.Count == 0
            ? new[] { "Vorschau folgt in Kuerze" }
            : _activeDetailAddon.Screenshots;
        AddonDetailPermissions.ItemsSource = BuildPermissionLabels(_activeDetailAddon);
        AddonDetailHosts.ItemsSource = _activeDetailAddon.HostPermissions.Count == 0 ? new[] { "Keine Host-Berechtigungen" } : _activeDetailAddon.HostPermissions;
        AddonDetailChangelog.Text = string.IsNullOrWhiteSpace(_activeDetailAddon.Changelog)
            ? "Noch kein Changelog hinterlegt."
            : _activeDetailAddon.Changelog;
    }

    private static List<string> BuildPermissionLabels(AddonItem item)
    {
        var labels = new List<string>();
        if (item.Permissions.Contains("activeTab"))
        {
            labels.Add("Zugriff auf aktuelle Seite");
            labels.Add("Tabs lesen");
        }

        if (item.Permissions.Contains("storage"))
        {
            labels.Add("Speicher verwenden");
        }

        if (item.Permissions.Contains("downloads"))
        {
            labels.Add("Downloads verwalten");
        }

        if (item.Permissions.Contains("notifications"))
        {
            labels.Add("Benachrichtigungen");
        }

        labels.AddRange(item.Permissions.Where(permission => labels.All(label => !label.Contains(permission, StringComparison.OrdinalIgnoreCase))));
        return labels.Count == 0 ? new List<string> { "Kein Zugriff erforderlich" } : labels;
    }

    private void ShowTrackerToast(int trackerCount)
    {
        TrackerToastCount.Text = trackerCount == 1
            ? "1 Tracker"
            : $"{trackerCount} Tracker";
        TrackerToast.Visibility = Visibility.Visible;

        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.1))));
        animation.Completed += (_, _) => TrackerToast.Visibility = Visibility.Collapsed;
        TrackerToast.BeginAnimation(OpacityProperty, animation);
    }

    private static int GetTrackerCountHint(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return 0;
        }

        var host = uri.Host.ToLowerInvariant();
        if (CommonTrackerHosts.Any(tracker => host.Contains(tracker, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        return host.Contains("google", StringComparison.OrdinalIgnoreCase) ||
               host.Contains("youtube", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 0;
    }

    private void SearchEngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SearchEngineBox.SelectedIndex < 0)
        {
            return;
        }

        _settingsService.Current.SearchEngine = (SearchEngine)SearchEngineBox.SelectedIndex;
        SettingsSearchEngineBox.SelectedIndex = SearchEngineBox.SelectedIndex;
        _settingsService.Save();
    }

    private void SettingsSearchEngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SettingsSearchEngineBox.SelectedIndex < 0)
        {
            return;
        }

        _settingsService.Current.SearchEngine = (SearchEngine)SettingsSearchEngineBox.SelectedIndex;
        SearchEngineBox.SelectedIndex = SettingsSearchEngineBox.SelectedIndex;
        _settingsService.Save();
    }

    private void BookmarkBarCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settingsService.Current.ShowBookmarkBar = BookmarkBarCheckBox.IsChecked == true;
        UpdateBookmarkBarVisibility();
        _settingsService.Save();
    }

    private void PrivacySettingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settingsService.Current.TrackerBlockerEnabled = TrackerBlockerCheckBox.IsChecked == true;
        _settingsService.Current.AggressiveTrackerBlockerEnabled = AggressiveTrackerBlockerCheckBox.IsChecked == true;
        _settingsService.Current.HttpsOnlyModeEnabled = HttpsOnlyModeCheckBox.IsChecked == true;
        _settingsService.Current.TabSleepEnabled = TabSleepCheckBox.IsChecked == true;
        _settingsService.Current.HardwareAccelerationEnabled = HardwareAccelerationCheckBox.IsChecked == true;
        _settingsService.Current.EcoModeEnabled = EcoModeCheckBox.IsChecked == true;
        _settingsService.Current.SmartSessionRestoreEnabled = SmartSessionRestoreCheckBox.IsChecked == true;
        _settingsService.Current.GlobalFingerprintingProtectionEnabled = GlobalFingerprintingCheckBox.IsChecked == true;
        _settingsService.Current.AutomaticProtectionEnabled = AutomaticProtectionCheckBox.IsChecked == true;
        _settingsService.Current.DarkModeEnforcerEnabled = DarkModeEnforcerCheckBox.IsChecked == true;
        _settingsService.Current.LazyMediaLoadingEnabled = LazyMediaLoadingCheckBox.IsChecked == true;
        _settingsService.Save();

        StatusText.Text = "Datenschutz- und Performance-Einstellungen gespeichert.";
    }

    private void ClearTrackerData_Click(object sender, RoutedEventArgs e)
    {
        ShowTrackerToast(0);
        StatusText.Text = "Tracker-Blocker-Daten wurden zurueckgesetzt.";
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _isClosing = true;

        try
        {
            CloseTransientPanels();
            BrowserHost.Content = null;

            if (!_isPrivateWindow)
            {
                SaveSession();
            }

            foreach (var tab in Tabs.ToList())
            {
                SafeDisposeBrowser(tab, "window-closing");
            }
        }
        catch (Exception ex)
        {
            App.LogException("window-closing", ex);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class TelegramProbeData
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? Text { get; set; }
        public List<string>? Headings { get; set; }
        public List<string>? Links { get; set; }
    }
}

public sealed class VisibilityToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }
}

public sealed class OmniboxSuggestion
{
    public OmniboxSuggestion(string icon, string title, string subtitle, string target, string badge)
    {
        Icon = icon;
        Title = title;
        Subtitle = subtitle;
        Target = target;
        Badge = badge;
    }

    public string Icon { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Target { get; }
    public string Badge { get; }
    public string DisplayCompletion { get; init; } = "";

    public static OmniboxSuggestion FromHistory(HistoryItem item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;
        var icon = item.Url.Contains("youtube", StringComparison.OrdinalIgnoreCase)
            ? "\uE714"
            : "\uE81C";
        return new OmniboxSuggestion(icon, title, item.Url, item.Url, "Verlauf")
        {
            DisplayCompletion = SimplifyTarget(item.Url)
        };
    }

    public static OmniboxSuggestion FromBookmark(Bookmark item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;
        return new OmniboxSuggestion("\uE734", title, item.Url, item.Url, "Lesezeichen")
        {
            DisplayCompletion = SimplifyTarget(item.Url)
        };
    }

    public static OmniboxSuggestion FromTab(BrowserTab item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? item.Url : item.Title;
        return new OmniboxSuggestion("\uE8A7", title, item.Url, item.Url, "Tab")
        {
            DisplayCompletion = SimplifyTarget(item.Url)
        };
    }

    private static string SimplifyTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return target;
        }

        if (uri.Scheme == "nova")
        {
            return target;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return host + uri.PathAndQuery.TrimEnd('/');
    }
}
