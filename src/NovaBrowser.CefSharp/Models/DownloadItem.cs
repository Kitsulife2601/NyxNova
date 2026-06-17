using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaBrowser.App.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    private int _id;
    private string _fileName = "";
    private string _fullPath = "";
    private string _url = "";
    private long _receivedBytes;
    private long _totalBytes;
    private bool _isComplete;
    private bool _isCancelled;
    private DateTime _startedAt = DateTime.Now;

    public int Id { get => _id; set => SetField(ref _id, value); }
    public string FileName { get => _fileName; set => SetField(ref _fileName, value); }
    public string FullPath { get => _fullPath; set => SetField(ref _fullPath, value); }
    public string Url { get => _url; set => SetField(ref _url, value); }
    public long ReceivedBytes { get => _receivedBytes; set { if (SetField(ref _receivedBytes, value)) NotifyProgressChanged(); } }
    public long TotalBytes { get => _totalBytes; set { if (SetField(ref _totalBytes, value)) NotifyProgressChanged(); } }
    public bool IsComplete { get => _isComplete; set { if (SetField(ref _isComplete, value)) NotifyProgressChanged(); } }
    public bool IsCancelled { get => _isCancelled; set { if (SetField(ref _isCancelled, value)) NotifyProgressChanged(); } }
    public DateTime StartedAt { get => _startedAt; set => SetField(ref _startedAt, value); }

    public int Progress => TotalBytes <= 0 ? 0 : (int)Math.Clamp(ReceivedBytes * 100 / TotalBytes, 0, 100);
    public string Status => IsCancelled ? "Abgebrochen" : IsComplete ? "Fertig" : $"{Progress}%";
    public bool IsActive => !IsComplete && !IsCancelled;

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

    private void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsActive));
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
