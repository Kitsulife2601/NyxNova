using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CefSharp;
using CefSharp.Wpf;

namespace NovaBrowser.App;

public partial class App : Application
{
    public const string ChromeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36";
    public static string? StartupUrl { get; private set; }
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaBrowser.CefSharp");
    public static string LogsRoot => Path.Combine(DataRoot, "Logs");
    public static string CrashLogPath => Path.Combine(LogsRoot, "nova-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        InstallCrashGuards();
        StartupUrl = e.Args.FirstOrDefault(arg => Uri.TryCreate(arg, UriKind.Absolute, out var uri) &&
                                                  (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "nova" || uri.Scheme == "novabrowser"));
        ConfigureCulture();
        ConfigureCef();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Cef.Shutdown();
        }
        catch (Exception ex)
        {
            LogException("cef-shutdown", ex);
        }

        base.OnExit(e);
    }

    public static void LogException(string source, Exception ex)
    {
        WriteCrashLine($"{DateTimeOffset.Now:u}\t{source}\t{ex}");
    }

    public static void LogMessage(string source, string message)
    {
        WriteCrashLine($"{DateTimeOffset.Now:u}\t{source}\t{message}");
    }

    private void InstallCrashGuards()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogException("dispatcher", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var details = args.ExceptionObject is Exception ex ? ex.ToString() : args.ExceptionObject?.ToString() ?? "Unbekannter Fehler";
            LogMessage("appdomain", details);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("task", args.Exception);
            args.SetObserved();
        };
    }

    private static void ConfigureCulture()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static void ConfigureCef()
    {
        var rootCachePath = Path.Combine(DataRoot, "CefRoot");
        var cachePath = Path.Combine(rootCachePath, "MainProfile");
        Directory.CreateDirectory(cachePath);
        Directory.CreateDirectory(LogsRoot);

        var settings = new CefSettings
        {
            CachePath = cachePath,
            RootCachePath = rootCachePath,
            PersistSessionCookies = true,
            Locale = "de",
            AcceptLanguageList = "de-DE,de,en-US,en",
            UserAgent = ChromeUserAgent,
            LogFile = Path.Combine(LogsRoot, "cef.log"),
            LogSeverity = LogSeverity.Warning
        };

        // Sicherheitsbewusst: keine no-sandbox, keine deaktivierte Web-Sicherheit und keine Anti-Detection-Hacks.
        // Unity-/WebGL-Seiten starten Audio oft erst nach Nutzeraktion. Diese Chrome-Optionen halten
        // Web-Sicherheit aktiv, erlauben aber normales Medienverhalten wie in einem Desktop-Browser.
        // Kamera/Mikrofon laufen bewusst ueber NovaPermissionHandler, damit Seiten erst nachfragen.
        settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
        settings.CefCommandLineArgs["enable-webgl"] = "1";
        settings.CefCommandLineArgs["enable-gpu-rasterization"] = "1";
        settings.CefCommandLineArgs["enable-zero-copy"] = "1";
        settings.CefCommandLineArgs["enable-accelerated-video-decode"] = "1";
        settings.CefCommandLineArgs["ignore-gpu-blocklist"] = "1";
        settings.CefCommandLineArgs["disk-cache-size"] = "536870912";
        settings.CefCommandLineArgs["media-cache-size"] = "268435456";
        settings.CefCommandLineArgs["enable-quic"] = "1";
        settings.CefCommandLineArgs["enable-smooth-scrolling"] = "1";

        Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null);
    }

    private static void WriteCrashLine(string line)
    {
        try
        {
            Directory.CreateDirectory(LogsRoot);
            File.AppendAllText(CrashLogPath, line + Environment.NewLine);
        }
        catch
        {
            // Crash-Logging darf die App nie selbst zu Fall bringen.
        }
    }
}
