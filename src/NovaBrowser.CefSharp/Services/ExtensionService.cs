using System.Collections.ObjectModel;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class ExtensionService
{
    private readonly JsonStore<List<ExtensionItem>> _store = new("extensions.json");

    public ObservableCollection<ExtensionItem> Items { get; } = new();
    public IEnumerable<ExtensionItem> Pinned => Items.Where(item => item.Enabled && item.Pinned);

    public void Load()
    {
        Items.Clear();
        var saved = _store.Load();
        if (saved.Count == 0)
        {
            saved = CreateDefaults();
        }

        foreach (var item in saved.OrderBy(item => item.Name))
        {
            Items.Add(item);
        }

        Save();
    }

    public void ToggleEnabled(ExtensionItem item)
    {
        item.Enabled = !item.Enabled;
        Save();
    }

    public void TogglePinned(ExtensionItem item)
    {
        item.Pinned = !item.Pinned;
        Save();
    }

    public void Remove(ExtensionItem item)
    {
        Items.Remove(item);
        Save();
    }

    public ExtensionItem AddFromUrl(string url)
    {
        var name = "Nova Extension";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            name = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        }

        var item = new ExtensionItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Version = "1.0.0",
            Description = "Added from URL.",
            Enabled = true,
            Pinned = false,
            HomepageUrl = url,
            PopupUrl = url,
            OptionsUrl = "nova://extensions",
            Permissions = new List<string> { "activeTab", "storage" },
            HostPermissions = new List<string> { url },
            Icon = string.Concat(name.Take(2)).ToUpperInvariant()
        };

        Items.Add(item);
        Save();
        return item;
    }

    public void Save() => _store.Save(Items.ToList());

    private static List<ExtensionItem> CreateDefaults()
    {
        return new List<ExtensionItem>
        {
            new()
            {
                Id = "nova-reader",
                Name = "Nova Reader",
                Version = "1.0.0",
                Description = "Improves reading pages with a calmer layout.",
                Enabled = true,
                Pinned = true,
                HomepageUrl = "nova://extensions",
                Permissions = new List<string> { "activeTab", "scripting" },
                HostPermissions = new List<string> { "https://*/*" },
                Icon = "NR",
                PopupUrl = "nova://reader-popup",
                OptionsUrl = "nova://extensions"
            },
            new()
            {
                Id = "nova-privacy",
                Name = "Privacy Guard",
                Version = "1.0.0",
                Description = "Shows privacy status for the active tab.",
                Enabled = true,
                Pinned = true,
                HomepageUrl = "nova://settings",
                Permissions = new List<string> { "cookies", "storage" },
                HostPermissions = new List<string> { "https://*/*", "http://*/*" },
                Icon = "PG",
                PopupUrl = "nova://privacy-popup",
                OptionsUrl = "nova://extensions"
            }
        };
    }
}
