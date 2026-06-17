namespace NovaBrowser.App.Models;

public sealed class ExtensionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Extension";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Pinned { get; set; }
    public string HomepageUrl { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public List<string> HostPermissions { get; set; } = new();
    public string Icon { get; set; } = "EX";
    public string PopupUrl { get; set; } = "";
    public string OptionsUrl { get; set; } = "";
}
