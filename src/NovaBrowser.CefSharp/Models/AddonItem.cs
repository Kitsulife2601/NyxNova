using System.Text.Json.Serialization;

namespace NovaBrowser.App.Models;

public sealed class AddonItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Nova Addon";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "Nova";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Nova Tools";
    public bool Enabled { get; set; } = true;
    public bool Pinned { get; set; }
    public bool Installed { get; set; }
    public string Icon { get; set; } = "\uECAA";
    public double Rating { get; set; } = 4.5;
    public string HomepageUrl { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public List<string> HostPermissions { get; set; } = new();
    public string PopupHtml { get; set; } = "";
    public string OptionsHtml { get; set; } = "";
    public List<string> Screenshots { get; set; } = new();
    public string Changelog { get; set; } = "";
    public string Source { get; set; } = "NovaStore";
    public string LocalPath { get; set; } = "";
    public bool CanModifyBrowser { get; set; }

    [JsonIgnore]
    public string PermissionSummary => HostPermissions.Count == 0
        ? "Kein Zugriff erforderlich"
        : $"Zugriff auf {HostPermissions.Count} Bereich(e)";

    [JsonIgnore]
    public string PinGlyph => Pinned ? "\uE718" : "\uE840";

    [JsonIgnore]
    public string PinMenuText => Pinned ? "Lösen" : "Anheften";

    [JsonIgnore]
    public string EnabledMenuText => Enabled ? "Deaktivieren" : "Aktivieren";
}
