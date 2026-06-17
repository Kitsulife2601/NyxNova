using System.Collections.ObjectModel;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class BookmarkService
{
    private readonly JsonStore<List<Bookmark>> _store = new("bookmarks.json");

    public ObservableCollection<Bookmark> Items { get; } = new();

    public void Load()
    {
        Items.Clear();
        foreach (var bookmark in _store.Load().OrderBy(item => item.Folder).ThenBy(item => item.Title))
        {
            Items.Add(bookmark);
        }
    }

    public bool Contains(string url) => Items.Any(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));

    public Bookmark? Find(string url) => Items.FirstOrDefault(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));

    public void SaveOrUpdate(string title, string url, string folder)
    {
        var existing = Find(url);
        if (existing is null)
        {
            Items.Add(new Bookmark { Title = title, Url = url, Folder = folder });
        }
        else
        {
            existing.Title = title;
            existing.Folder = folder;
        }

        Save();
    }

    public void Toggle(string title, string url)
    {
        var existing = Items.FirstOrDefault(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Items.Remove(existing);
        }
        else
        {
            Items.Add(new Bookmark { Title = title, Url = url });
        }

        Save();
    }

    public void Remove(Bookmark item)
    {
        Items.Remove(item);
        Save();
    }

    public void Save() => _store.Save(Items.ToList());
}
