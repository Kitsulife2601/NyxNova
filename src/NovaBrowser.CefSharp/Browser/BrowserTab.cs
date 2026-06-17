using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChromiumWebBrowser = CefSharp.Wpf.HwndHost.ChromiumWebBrowser;

namespace NovaBrowser.App.Browser;

public sealed class BrowserTab : INotifyPropertyChanged
{
    private ChromiumWebBrowser? _browser;
    private string _title = "Neuer Tab";
    private string _url = "";
    private bool _isSelected;
    private string _faviconText = "N";

    public BrowserTab(ChromiumWebBrowser? browser, string startUrl)
    {
        _browser = browser;
        Url = startUrl;
        Title = startUrl.Equals("nova://start", StringComparison.OrdinalIgnoreCase) ? "Start Page" : "Neuer Tab";
        UpdateFaviconText();
    }

    public ChromiumWebBrowser? Browser
    {
        get => _browser;
        set => SetField(ref _browser, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, string.IsNullOrWhiteSpace(value) ? "Neuer Tab" : value);
    }

    public string Url
    {
        get => _url;
        set
        {
            if (SetField(ref _url, value))
            {
                UpdateFaviconText();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            SetField(ref _isSelected, value);
        }
    }

    public string FaviconText
    {
        get => _faviconText;
        private set => SetField(ref _faviconText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpdateFaviconText()
    {
        if (Url.StartsWith("nova://", StringComparison.OrdinalIgnoreCase))
        {
            FaviconText = "N";
            return;
        }

        if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
        {
            FaviconText = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase).FirstOrDefault().ToString().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(FaviconText))
            {
                return;
            }
        }

        FaviconText = "N";
    }
}
