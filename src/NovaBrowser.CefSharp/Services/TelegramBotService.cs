using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.Services;

public sealed class TelegramBotService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NyxNova.TelegramBot.v1");
    private static readonly HttpClient HttpClient = new();
    private readonly JsonStore<Dictionary<string, TelegramBotSnapshot>> _snapshotStore = new("telegram-bot-snapshots.json");
    private readonly string _tokenPath;

    public TelegramBotService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NovaBrowser.CefSharp");
        Directory.CreateDirectory(root);
        _tokenPath = Path.Combine(root, "telegram-bot-token.dpapi");
    }

    public bool HasStoredToken => File.Exists(_tokenPath);

    public string? LoadToken()
    {
        try
        {
            if (!File.Exists(_tokenPath))
            {
                return null;
            }

            var protectedBytes = File.ReadAllBytes(_tokenPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public void SaveToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token.Trim());
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_tokenPath, protectedBytes);
    }

    public void DeleteToken()
    {
        if (File.Exists(_tokenPath))
        {
            File.Delete(_tokenPath);
        }
    }

    public TelegramBotSnapshot? GetSnapshot(string url)
    {
        var data = _snapshotStore.Load();
        return data.TryGetValue(NormalizeSnapshotKey(url), out var snapshot) ? snapshot : null;
    }

    public void SaveSnapshot(TelegramBotSnapshot snapshot)
    {
        snapshot.TextHash = ComputeHash(snapshot.Text);
        snapshot.CapturedAt = DateTime.Now;

        var data = _snapshotStore.Load();
        data[NormalizeSnapshotKey(snapshot.Url)] = snapshot;
        _snapshotStore.Save(data);
    }

    public IReadOnlyList<TelegramBotChange> Analyze(TelegramBotSnapshot? previous, TelegramBotSnapshot current)
    {
        current.TextHash = ComputeHash(current.Text);
        var changes = new List<TelegramBotChange>();

        if (previous is null)
        {
            changes.Add(new TelegramBotChange
            {
                Title = "Erster Snapshot gespeichert",
                Detail = "NyxNova hat diese Seite zum ersten Mal aufgenommen. Der naechste Check zeigt echte Aenderungen.",
                TargetUrl = current.Url,
                Icon = "\uE8A5"
            });
            return changes;
        }

        if (!string.Equals(previous.Title, current.Title, StringComparison.Ordinal))
        {
            changes.Add(new TelegramBotChange
            {
                Title = "Seitentitel geaendert",
                Detail = $"{previous.Title} -> {current.Title}",
                TargetUrl = current.Url,
                Icon = "\uE8D2"
            });
        }

        if (!string.Equals(previous.TextHash, current.TextHash, StringComparison.Ordinal))
        {
            var delta = current.Text.Length - previous.Text.Length;
            changes.Add(new TelegramBotChange
            {
                Title = "Inhalt geaendert",
                Detail = delta == 0
                    ? "Textinhalt wurde aktualisiert."
                    : $"Textumfang veraendert um {delta:+#;-#;0} Zeichen.",
                TargetUrl = current.Url,
                Icon = "\uE8D4"
            });
        }

        var newHeadings = current.Headings
            .Where(heading => !previous.Headings.Contains(heading, StringComparer.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        if (newHeadings.Count > 0)
        {
            changes.Add(new TelegramBotChange
            {
                Title = "Neue Bereiche gefunden",
                Detail = string.Join(" | ", newHeadings),
                TargetUrl = current.Url,
                Icon = "\uE8FD"
            });
        }

        var linkDelta = current.Links.Count - previous.Links.Count;
        if (linkDelta != 0)
        {
            changes.Add(new TelegramBotChange
            {
                Title = "Links aktualisiert",
                Detail = linkDelta > 0 ? $"{linkDelta} neue Links erkannt." : $"{Math.Abs(linkDelta)} Links weniger erkannt.",
                TargetUrl = current.Url,
                Icon = "\uE71B"
            });
        }

        if (changes.Count == 0)
        {
            changes.Add(new TelegramBotChange
            {
                Title = "Keine sichtbare Aenderung",
                Detail = "Screenshot und Textcheck zeigen keine klare Aenderung seit dem letzten Snapshot.",
                TargetUrl = current.Url,
                Icon = "\uE73E"
            });
        }

        return changes;
    }

    public async Task SendPhotoAsync(string token, string chatId, byte[] pngBytes, string caption, string? parseMode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Telegram Bot Token fehlt.");
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new InvalidOperationException("Telegram Chat ID fehlt.");
        }

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId.Trim()), "chat_id");
        form.Add(new StringContent(caption.Length > 1024 ? caption[..1021] + "..." : caption), "caption");
        if (!string.IsNullOrWhiteSpace(parseMode))
        {
            form.Add(new StringContent(parseMode.Trim()), "parse_mode");
        }
        form.Add(new ByteArrayContent(pngBytes), "photo", "nyxnova-page.png");

        using var response = await HttpClient.PostAsync($"https://api.telegram.org/bot{token.Trim()}/sendPhoto", form, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Telegram Fehler {(int)response.StatusCode}: {responseText}");
        }
    }

    public static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
        return Convert.ToHexString(bytes);
    }

    private static string NormalizeSnapshotKey(string url) => (url ?? "").Trim().ToLowerInvariant();
}
