using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CefSharp;
using CefSharp.Wpf;
using Microsoft.Win32;

namespace NovaBrowser.App;

public partial class App : Application
{
    public const string ChromeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36";
    public static string? StartupUrl { get; private set; }
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaBrowser.CefSharp");
    public static string LogsRoot => Path.Combine(DataRoot, "Logs");
    public static string CrashLogPath => Path.Combine(LogsRoot, "nova-crash.log");
    private static string[] _startupArgs = [];

    public static void SetStartupArgs(string[] args)
    {
        _startupArgs = args;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        InstallCrashGuards();
        EnsureWindowsUninstallEntry();
        var args = _startupArgs.Length > 0 ? _startupArgs : e.Args;
        StartupUrl = args.FirstOrDefault(arg => Uri.TryCreate(arg, UriKind.Absolute, out var uri) &&
                                                  (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "nova" || uri.Scheme == "novabrowser"));
        ConfigureCulture();
        ConfigureCef();
        StartSingleInstanceListener();
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
            LogSeverity = LogSeverity.Warning,
            BackgroundColor = Cef.ColorSetARGB(255, 11, 9, 17)
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

    private static void EnsureWindowsUninstallEntry()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            var appRoot = Directory.GetParent(exePath)?.FullName;
            if (string.IsNullOrWhiteSpace(appRoot))
            {
                return;
            }

            var updateExe = Path.Combine(appRoot, "Update.exe");
            if (!File.Exists(updateExe))
            {
                return;
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var uninstallCommand = $"\"{updateExe}\" uninstall";
            var quietUninstallCommand = $"\"{updateExe}\" uninstall --silent";
            var estimatedSizeKb = EstimateDirectorySizeKb(appRoot);

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NyxNova");
            if (key is null)
            {
                return;
            }

            key.SetValue("DisplayName", "NyxNova Browser", RegistryValueKind.String);
            key.SetValue("DisplayVersion", version, RegistryValueKind.String);
            key.SetValue("Publisher", "Kitsulife2601", RegistryValueKind.String);
            key.SetValue("InstallLocation", appRoot, RegistryValueKind.String);
            key.SetValue("DisplayIcon", exePath, RegistryValueKind.String);
            key.SetValue("UninstallString", uninstallCommand, RegistryValueKind.String);
            key.SetValue("QuietUninstallString", quietUninstallCommand, RegistryValueKind.String);
            key.SetValue("URLInfoAbout", "https://github.com/Kitsulife2601/NyxNova", RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (estimatedSizeKb > 0)
            {
                key.SetValue("EstimatedSize", estimatedSizeKb, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            LogException("uninstall-registry", ex);
        }
    }

    private static int EstimateDirectorySizeKb(string path)
    {
        try
        {
            var bytes = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });

            return (int)Math.Min(int.MaxValue, Math.Max(1, bytes / 1024));
        }
        catch
        {
            return 0;
        }
    }

    private static void StartSingleInstanceListener()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        Program.SingleInstancePipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var forwardedArgs = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync()) is not null)
                    {
                        forwardedArgs.Add(line);
                    }

                    var args = forwardedArgs.ToArray();
                    Current?.Dispatcher.BeginInvoke(() => HandleSecondInstanceArgs(args));
                }
                catch (Exception ex)
                {
                    LogException("single-instance-pipe", ex);
                    await Task.Delay(500);
                }
            }
        });
    }

    private static void HandleSecondInstanceArgs(string[] args)
    {
        if (Current?.MainWindow is not MainWindow window)
        {
            return;
        }

        var url = args.FirstOrDefault(arg => Uri.TryCreate(arg, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "nova" || uri.Scheme == "novabrowser"));

        window.ActivateFromSecondInstance(url);
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
