namespace NovaBrowser.App.Models;

public sealed class BrowserSettings
{
    public SearchEngine SearchEngine { get; set; } = SearchEngine.Google;
    public bool ShowBookmarkBar { get; set; } = false;
    public double ZoomLevel { get; set; } = 0;
    public string HomeUrl { get; set; } = "nova://start";
    public string Theme { get; set; } = "NovaNeon";
    public bool TrackerBlockerEnabled { get; set; } = true;
    public bool AggressiveTrackerBlockerEnabled { get; set; } = false;
    public bool HttpsOnlyModeEnabled { get; set; } = false;
    public bool TabSleepEnabled { get; set; } = true;
    public bool HardwareAccelerationEnabled { get; set; } = true;
    public bool EcoModeEnabled { get; set; } = false;
    public bool SmartSessionRestoreEnabled { get; set; } = true;
    public bool GlobalFingerprintingProtectionEnabled { get; set; } = true;
    public bool AutomaticProtectionEnabled { get; set; } = true;
    public bool DarkModeEnforcerEnabled { get; set; } = false;
    public bool LazyMediaLoadingEnabled { get; set; } = true;
    public bool TelegramBotEnabled { get; set; } = false;
    public string TelegramBotChatId { get; set; } = "";
}

public enum SearchEngine
{
    Google,
    DuckDuckGo,
    Bing
}
