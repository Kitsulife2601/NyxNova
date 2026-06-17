using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class DownloadService
{
    private readonly JsonStore<List<DownloadItem>> _store = new("downloads.json");

    public ObservableCollection<DownloadItem> Items { get; } = new();

    public void Load()
    {
        Items.Clear();
        foreach (var item in _store.Load().OrderByDescending(item => item.StartedAt).Take(200))
        {
            Items.Add(item);
        }
    }

    public void Upsert(DownloadItem item)
    {
        var existing = Items.FirstOrDefault(download => download.Id == item.Id);
        if (existing is null)
        {
            Items.Insert(0, item);
        }
        else
        {
            existing.FileName = item.FileName;
            existing.FullPath = item.FullPath;
            existing.Url = item.Url;
            existing.ReceivedBytes = item.ReceivedBytes;
            existing.TotalBytes = item.TotalBytes;
            existing.IsComplete = item.IsComplete;
            existing.IsCancelled = item.IsCancelled;
        }

        Save();
    }

    public void Remove(DownloadItem item)
    {
        Items.Remove(item);
        Save();
    }

    public void Clear()
    {
        Items.Clear();
        Save();
    }

    public static void ShowInFolder(DownloadItem item)
    {
        if (File.Exists(item.FullPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
        }
    }

    public static void Open(DownloadItem item)
    {
        if (File.Exists(item.FullPath))
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
    }

    public void Save() => _store.Save(Items.ToList());
}
