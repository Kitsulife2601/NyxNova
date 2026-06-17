namespace NovaBrowser.App.Models;

public sealed class BrowserSettings
{
    public SearchEngine SearchEngine { get; set; } = SearchEngine.Google;
    public bool ShowBookmarkBar { get; set; } = false;
    public double ZoomLevel { get; set; } = 0;
    public string HomeUrl { get; set; } = "nova://start";
}

public enum SearchEngine
{
    Google,
    DuckDuckGo,
    Bing
}
