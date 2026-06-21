using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class AddonService
{
    private static readonly HashSet<string> RemovedSampleAddonIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "nova-new-tab",
        "new-tab",
        "webseite-new-tab",
        "quick-notes",
        "privacy-guard",
        "discord-quick"
    };

    private static readonly Regex SafeAddonIdPattern = new("^[a-zA-Z0-9._-]{3,64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "storage",
        "tabs",
        "activeTab",
        "downloads",
        "notifications",
        "theme",
        "startPage",
        "browserUi",
        "localServer",
        "navigation",
        "translation"
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

        var validIds = catalog.Select(addon => addon.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedIds = _installedStore.Load()
            .Where(id => validIds.Contains(id) && !RemovedSampleAddonIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    public AddonItem ReadZipManifest(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            throw new InvalidOperationException("Die ZIP-Datei wurde nicht gefunden.");
        }

        var fileInfo = new FileInfo(zipPath);
        if (fileInfo.Length > 50L * 1024L * 1024L)
        {
            throw new InvalidOperationException("Das Addon-ZIP ist zu gross. Maximal erlaubt sind 50 MB.");
        }

        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry("nova-addon.json") ?? archive.GetEntry("manifest.json");
        if (manifestEntry is null)
        {
            throw new InvalidOperationException("Manifest fehlt. Erwartet wird nova-addon.json oder manifest.json.");
        }

        using var manifestStream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<AddonManifest>(manifestStream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Manifest konnte nicht gelesen werden.");

        ValidateManifest(manifest, archive);

        return new AddonItem
        {
            Id = manifest.Id.Trim(),
            Name = manifest.Name.Trim(),
            Version = string.IsNullOrWhiteSpace(manifest.Version) ? "1.0.0" : manifest.Version.Trim(),
            Author = string.IsNullOrWhiteSpace(manifest.Author) ? "Unbekannt" : manifest.Author.Trim(),
            Description = manifest.Description?.Trim() ?? "",
            Category = string.IsNullOrWhiteSpace(manifest.Category) ? "ZIP Addon" : manifest.Category.Trim(),
            Enabled = true,
            Installed = false,
            Pinned = false,
            Icon = string.IsNullOrWhiteSpace(manifest.Icon) ? "\uECAA" : manifest.Icon.Trim(),
            HomepageUrl = manifest.HomepageUrl?.Trim() ?? "",
            Permissions = (manifest.Permissions ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            HostPermissions = (manifest.HostPermissions ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            PopupHtml = manifest.PopupHtml?.Trim() ?? "",
            OptionsHtml = manifest.OptionsHtml?.Trim() ?? "",
            Screenshots = manifest.Screenshots.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Changelog = manifest.Changelog?.Trim() ?? "Importiert aus ZIP.",
            Source = "ZIP",
            CanModifyBrowser = (manifest.Permissions ?? new List<string>()).Any(permission =>
                permission.Equals("startPage", StringComparison.OrdinalIgnoreCase) ||
                permission.Equals("browserUi", StringComparison.OrdinalIgnoreCase) ||
                permission.Equals("theme", StringComparison.OrdinalIgnoreCase))
        };
    }

    public AddonItem InstallFromZip(string zipPath, AddonItem preview)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var addonRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NovaBrowser.CefSharp",
            "Addons",
            preview.Id);

        if (Directory.Exists(addonRoot))
        {
            Directory.Delete(addonRoot, recursive: true);
        }

        Directory.CreateDirectory(addonRoot);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(addonRoot, entry.FullName));
            if (!destination.StartsWith(Path.GetFullPath(addonRoot), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("ZIP enthaelt ungueltige Pfade.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        var existing = Catalog.FirstOrDefault(addon => addon.Id.Equals(preview.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Catalog.Remove(existing);
            Installed.Remove(existing);
        }

        preview.Installed = true;
        preview.Enabled = true;
        preview.LocalPath = addonRoot;
        Catalog.Add(preview);
        Installed.Add(preview);
        Save();
        return preview;
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

    private static void ValidateManifest(AddonManifest manifest, ZipArchive archive)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || !SafeAddonIdPattern.IsMatch(manifest.Id))
        {
            throw new InvalidOperationException("Addon-ID fehlt oder ist ungueltig. Erlaubt sind 3-64 Zeichen: Buchstaben, Zahlen, Punkt, Unterstrich, Minus.");
        }

        if (RemovedSampleAddonIds.Contains(manifest.Id))
        {
            throw new InvalidOperationException("Diese Addon-ID ist gesperrt, weil sie zu alten Beispiel-Addons gehoert.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException("Addon-Name fehlt.");
        }

        manifest.Permissions ??= new List<string>();
        manifest.HostPermissions ??= new List<string>();

        var unknownPermissions = manifest.Permissions
            .Where(permission => !AllowedPermissions.Contains(permission))
            .ToList();
        if (unknownPermissions.Count > 0)
        {
            throw new InvalidOperationException($"Unbekannte Berechtigung: {string.Join(", ", unknownPermissions)}");
        }

        foreach (var host in manifest.HostPermissions)
        {
            if (!IsAllowedHostPermission(host))
            {
                throw new InvalidOperationException($"Ungueltiger Host-Zugriff: {host}");
            }
        }

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(entry.FullName) ||
                entry.FullName.Contains('\\'))
            {
                throw new InvalidOperationException("ZIP enthaelt ungueltige oder unsichere Dateipfade.");
            }
        }
    }

    private static bool IsAllowedHostPermission(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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

    private sealed class AddonManifest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Icon { get; set; } = "";
        public string HomepageUrl { get; set; } = "";
        public List<string> Permissions { get; set; } = new();
        public List<string> HostPermissions { get; set; } = new();
        public string PopupHtml { get; set; } = "";
        public string OptionsHtml { get; set; } = "";
        public List<string> Screenshots { get; set; } = new();
        public string Changelog { get; set; } = "";
    }
}
