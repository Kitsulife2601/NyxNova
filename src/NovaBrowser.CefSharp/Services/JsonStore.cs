using System.IO;
using System.Text.Json;

namespace NovaBrowser.App.Services;

public sealed class JsonStore<T> where T : new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;

    public JsonStore(string fileName)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NovaBrowser.CefSharp");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, fileName);
    }

    public T Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new T();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public void Save(T data)
    {
        var json = JsonSerializer.Serialize(data, Options);
        File.WriteAllText(_filePath, json);
    }
}
