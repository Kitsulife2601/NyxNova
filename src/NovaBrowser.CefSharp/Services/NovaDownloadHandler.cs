using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CefSharp;
using NovaBrowser.App.Models;
using CefDownloadItem = CefSharp.DownloadItem;
using NovaDownloadItem = NovaBrowser.App.Models.DownloadItem;

namespace NovaBrowser.App.Services;

public sealed class NovaDownloadHandler : IDownloadHandler
{
    private readonly DownloadService _downloads;
    private readonly Dispatcher _dispatcher;

    public NovaDownloadHandler(DownloadService downloads, Dispatcher dispatcher)
    {
        _downloads = downloads;
        _dispatcher = dispatcher;
    }

    public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    public bool OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, CefDownloadItem downloadItem, IBeforeDownloadCallback callback)
    {
        if (callback.IsDisposed)
        {
            return true;
        }

        var downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloadsFolder);
        var safeName = string.IsNullOrWhiteSpace(downloadItem.SuggestedFileName)
            ? $"nova-download-{downloadItem.Id}"
            : downloadItem.SuggestedFileName;
        var targetPath = Path.Combine(downloadsFolder, safeName);

        _dispatcher.BeginInvoke(() => Upsert(downloadItem, targetPath));

        // showDialog:false speichert stabil in Downloads; der Download-Manager zeigt danach den Pfad.
        callback.Continue(targetPath, showDialog: false);
        return true;
    }

    public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefDownloadItem downloadItem, IDownloadItemCallback callback)
    {
        _dispatcher.BeginInvoke(() => Upsert(downloadItem, downloadItem.FullPath));
    }

    private void Upsert(CefDownloadItem downloadItem, string fullPath)
    {
        var existing = _downloads.Items.FirstOrDefault(item => item.Id == downloadItem.Id);
        if (existing is null)
        {
            existing = new NovaDownloadItem { Id = downloadItem.Id };
        }

        existing.FileName = Path.GetFileName(fullPath);
        existing.FullPath = fullPath;
        existing.Url = downloadItem.Url;
        existing.ReceivedBytes = downloadItem.ReceivedBytes;
        existing.TotalBytes = downloadItem.TotalBytes;
        existing.IsComplete = downloadItem.IsComplete;
        existing.IsCancelled = downloadItem.IsCancelled;
        _downloads.Upsert(existing);
    }
}
