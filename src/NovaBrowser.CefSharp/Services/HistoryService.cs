using System.Collections.ObjectModel;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class HistoryService
{
    private readonly JsonStore<List<HistoryItem>> _store = new("history.json");

    public ObservableCollection<HistoryItem> Items { get; } = new();

    public void Load()
    {
        Items.Clear();
        foreach (var item in _store.Load().OrderByDescending(item => item.VisitedAt).Take(500))
        {
            Items.Add(item);
        }
    }

    public void Record(string title, string url)
    {
        if (!AddressParser.IsWebUrl(url))
        {
            return;
        }

        var existing = Items.FirstOrDefault(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Title = title;
            existing.VisitedAt = DateTime.Now;
            Items.Move(Items.IndexOf(existing), 0);
        }
        else
        {
            Items.Insert(0, new HistoryItem { Title = title, Url = url, VisitedAt = DateTime.Now });
        }

        while (Items.Count > 500)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        Save();
    }

    public void Remove(HistoryItem item)
    {
        Items.Remove(item);
        Save();
    }

    public void Clear()
    {
        Items.Clear();
        Save();
    }

    public void Save() => _store.Save(Items.ToList());
}
