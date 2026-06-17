using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class SettingsService
{
    private readonly JsonStore<BrowserSettings> _store = new("settings.json");

    public BrowserSettings Current { get; private set; } = new();

    public void Load() => Current = _store.Load();
    public void Save() => _store.Save(Current);
}
