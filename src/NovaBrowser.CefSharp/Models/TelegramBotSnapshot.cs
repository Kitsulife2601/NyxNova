namespace NovaBrowser.App.Models;

public sealed class TelegramBotSnapshot
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextHash { get; set; } = "";
    public List<string> Headings { get; set; } = new();
    public List<string> Links { get; set; } = new();
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}
