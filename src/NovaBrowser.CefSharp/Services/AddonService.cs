using System.Collections.ObjectModel;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class AddonService
{
    private static readonly HashSet<string> RemovedSampleAddonIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "nova-new-tab",
        "new-tab",
        "quick-notes",
        "privacy-guard",
        "discord-quick"
    };

    private static readonly HashSet<string> HiddenToolbarAddonIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "webseite-new-tab",
        "nova-new-tab",
        "new-tab"
    };

    private readonly JsonStore<List<AddonItem>> _catalogStore = new("addons.json");
    private readonly JsonStore<List<string>> _installedStore = new("installed-addons.json");
    private readonly JsonStore<Dictionary<string, Dictionary<string, string>>> _settingsStore = new("addon-settings.json");

    public ObservableCollection<AddonItem> Catalog { get; } = new();
    public ObservableCollection<AddonItem> Installed { get; } = new();
    public Dictionary<string, Dictionary<string, string>> Settings { get; private set; } = new();

    public IEnumerable<AddonItem> Pinned => Installed.Where(addon => addon.Enabled && addon.Pinned && !IsToolbarHidden(addon));

    public void Load()
    {
        Catalog.Clear();
        Installed.Clear();

        var defaults = CreateDefaultCatalog();
        var catalog = _catalogStore.Load()
            .Where(addon => !RemovedSampleAddonIds.Contains(addon.Id))
            .ToList();

        if (catalog.Count == 0)
        {
            catalog = defaults;
        }
        else
        {
            foreach (var defaultAddon in defaults)
            {
                var existing = catalog.FirstOrDefault(addon => addon.Id.Equals(defaultAddon.Id, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    catalog.Add(defaultAddon);
                }
                else
                {
                    ApplyDefaultMetadata(existing, defaultAddon);
                }
            }
        }

        var validIds = defaults.Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedIds = _installedStore.Load()
            .Where(id => validIds.Contains(id) && !RemovedSampleAddonIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (installedIds.Count == 0)
        {
            installedIds = defaults.Where(addon => addon.Installed).Select(addon => addon.Id).ToList();
        }
        else
        {
            foreach (var defaultInstalled in defaults.Where(addon => addon.Installed))
            {
                if (!installedIds.Contains(defaultInstalled.Id, StringComparer.OrdinalIgnoreCase))
                {
                    installedIds.Add(defaultInstalled.Id);
                }
            }
        }

        Settings = _settingsStore.Load();

        foreach (var addon in catalog.OrderBy(addon => addon.Name))
        {
            addon.Installed = installedIds.Contains(addon.Id, StringComparer.OrdinalIgnoreCase);
            addon.Enabled = addon.Installed && addon.Enabled;
            addon.Pinned = addon.Installed && addon.Pinned;
            if (IsToolbarHidden(addon))
            {
                addon.Pinned = false;
            }
            Catalog.Add(addon);
            if (addon.Installed)
            {
                Installed.Add(addon);
            }
        }

        Save();
    }

    public bool Install(string id)
    {
        var addon = Catalog.FirstOrDefault(item => item.Id == id);
        if (addon is null || addon.Installed)
        {
            return false;
        }

        addon.Installed = true;
        addon.Enabled = true;
        Installed.Add(addon);
        Save();
        return true;
    }

    public void Remove(AddonItem addon)
    {
        addon.Installed = false;
        addon.Enabled = false;
        addon.Pinned = false;
        Installed.Remove(addon);
        Save();
    }

    public void ToggleEnabled(AddonItem addon)
    {
        addon.Enabled = !addon.Enabled;
        Save();
    }

    public void TogglePinned(AddonItem addon)
    {
        if (IsToolbarHidden(addon))
        {
            addon.Pinned = false;
            Save();
            return;
        }

        addon.Pinned = !addon.Pinned;
        Save();
    }

    public AddonItem? Find(string id) => Catalog.FirstOrDefault(addon => addon.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void Save()
    {
        _catalogStore.Save(Catalog.ToList());
        _installedStore.Save(Installed.Select(addon => addon.Id).ToList());
        _settingsStore.Save(Settings);
    }

    private static List<AddonItem> CreateDefaultCatalog()
    {
        return new List<AddonItem>
        {
            Addon("webseite-new-tab", "Neuer Tab", "Dennis", "Moderne Nova-Startseite aus webseite-adon.zip mit Uhr, Suchfeld, Quick Links, Hintergrundwechsel, Uebersetzung und Heart-Server.", "Nova Tools", true, false, "\uE80F", 4.9, new[] { "Speicher verwenden", "Navigation fuer optionale Uebersetzung erkennen", "Zugriff auf lokalen Heart-Server 127.0.0.1:8787" }, new[] { "http://127.0.0.1:8787/*" }),
            Addon("nova-translate", "Nova Uebersetzer", "Nova Labs", "Uebersetzt markierte Texte und Webseiten.", "Produktivitaet", false, false, "\uE774", 4.6, new[] { "Aktuelle Seite lesen", "Speicher verwenden" }, new[] { "https://*/*" }),
            Addon("youtube-tools", "YouTube Tools", "Nova Media", "Schnelle Werkzeuge fuer YouTube.", "Musik", false, false, "\uE768", 4.4, new[] { "Aktuelle Seite lesen", "Speicher verwenden" }, new[] { "https://youtube.com/*", "https://www.youtube.com/*" }),
            Addon("vrchat-tools", "VRChat Schnellbereich", "Nova Social", "VRChat-Links, Wiki und Community-Zugriff als eigene Nova-Karte.", "Social", false, false, "\uE716", 4.7, new[] { "Tabs lesen", "Speicher verwenden" }, new[] { "https://vrchat.com/*", "https://wiki.vrchat.com/*" }),
            Addon("theme-switcher", "Theme Switcher", "Nova Design", "Wechselt zwischen Nova-Farbstimmungen.", "Design", true, false, "\uE708", 4.5, new[] { "Speicher verwenden" }, Array.Empty<string>()),
            Addon("download-helper", "Download Helfer", "Nova Tools", "Hilft beim Sortieren und Wiederfinden von Downloads.", "Entwickler", false, false, "\uE896", 4.3, new[] { "Downloads verwalten", "Speicher verwenden" }, Array.Empty<string>())
        };
    }

    private static bool IsToolbarHidden(AddonItem addon)
    {
        return HiddenToolbarAddonIds.Contains(addon.Id) ||
               addon.Icon == "\uE80F" ||
               addon.Name.Contains("Neuer Tab", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyDefaultMetadata(AddonItem target, AddonItem source)
    {
        target.Name = source.Name;
        target.Version = source.Version;
        target.Author = source.Author;
        target.Description = source.Description;
        target.Category = source.Category;
        target.Icon = source.Icon;
        target.Rating = source.Rating;
        target.HomepageUrl = source.HomepageUrl;
        target.Permissions = source.Permissions;
        target.HostPermissions = source.HostPermissions;
        target.PopupHtml = source.PopupHtml;
        target.OptionsHtml = source.OptionsHtml;
        target.Screenshots = source.Screenshots;
        target.Changelog = source.Changelog;
    }

    private static AddonItem Addon(string id, string name, string author, string description, string category, bool installed, bool pinned, string icon, double rating, IEnumerable<string> permissions, IEnumerable<string> hosts)
    {
        return new AddonItem
        {
            Id = id,
            Name = name,
            Version = "1.0.0",
            Author = author,
            Description = description,
            Category = category,
            Enabled = installed,
            Pinned = pinned,
            Installed = installed,
            Icon = icon,
            Rating = rating,
            HomepageUrl = $"nova://store/addon/{id}",
            Permissions = permissions.ToList(),
            HostPermissions = hosts.ToList(),
            PopupHtml = $"<h2>{name}</h2><p>{description}</p>",
            OptionsHtml = $"<h2>{name} Optionen</h2>",
            Screenshots = new List<string> { "Screenshot Platzhalter 1", "Screenshot Platzhalter 2" },
            Changelog = "1.0.0 - Erste NovaStore-Version."
        };
    }
}
