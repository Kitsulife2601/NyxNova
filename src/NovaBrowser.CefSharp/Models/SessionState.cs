namespace NovaBrowser.App.Models;

public sealed class SessionState
{
    public List<string> OpenTabs { get; set; } = new();
    public string? ActiveTabUrl { get; set; }
}
