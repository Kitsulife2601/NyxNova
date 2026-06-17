namespace NovaBrowser.App.Services;

using NovaBrowser.App.Models;

public static class AddressParser
{
    public const string HomeUrl = "nova://start";

    public static string Normalize(string input, SearchEngine searchEngine = SearchEngine.Google)
    {
        var text = (input ?? "").Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return HomeUrl;
        }

        if (text.Equals("nova://newtab", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("nova://start", StringComparison.OrdinalIgnoreCase))
        {
            return HomeUrl;
        }

        if (IsAuthCallbackUrl(text))
        {
            return ToInternalAuthCallbackUrl(text);
        }

        if (IsInternalUrl(text))
        {
            return text.ToLowerInvariant();
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (LooksLikeDomain(text))
        {
            return "https://" + text;
        }

        return BuildSearchUrl(text, searchEngine);
    }

    public static bool IsInternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var value = StripSuffix(url.Trim()).ToLowerInvariant();
        return value is "nova://start" or "nova://settings" or "nova://history" or "nova://downloads" or "nova://diagnostics" or "nova://media-diagnostics" or "nova://extensions" or "nova://store" or "nova://update" or "nova://auth/callback" ||
               value.StartsWith("nova://store/addon/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAuthCallbackUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               uri.Scheme.Equals("novabrowser", StringComparison.OrdinalIgnoreCase) &&
               uri.Host.Equals("auth", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.Equals("/callback", StringComparison.OrdinalIgnoreCase);
    }

    public static string ToInternalAuthCallbackUrl(string callbackUrl)
    {
        var suffix = "";
        if (Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri))
        {
            suffix = uri.Query + uri.Fragment;
        }

        return "nova://auth/callback" + suffix;
    }

    public static bool IsWebUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    public static string BuildSearchUrl(string query, SearchEngine searchEngine)
    {
        var escaped = Uri.EscapeDataString(query);
        return searchEngine switch
        {
            SearchEngine.DuckDuckGo => $"https://duckduckgo.com/?q={escaped}",
            SearchEngine.Bing => $"https://www.bing.com/search?q={escaped}",
            _ => $"https://www.google.com/search?q={escaped}"
        };
    }

    private static bool LooksLikeDomain(string text)
    {
        return !text.Contains(' ') &&
               text.Contains('.') &&
               !text.StartsWith('.') &&
               !text.EndsWith('.');
    }

    private static string StripSuffix(string url)
    {
        var index = url.IndexOfAny(['?', '#']);
        return index >= 0 ? url[..index] : url;
    }
}
