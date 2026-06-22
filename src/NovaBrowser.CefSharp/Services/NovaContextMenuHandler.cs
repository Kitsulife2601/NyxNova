using CefSharp;
using CefSharp.Handler;

namespace NovaBrowser.App.Services;

// Unterdrueckt das native CEF-Kontextmenue und meldet den Klick-Kontext
// (Link / Bild / Seite) an die App, die ein eigenes Neon-Menue (WPF-Popup) zeigt.
public sealed class NovaContextMenuHandler : ContextMenuHandler
{
    private readonly Action<string, string, string> _showMenu;

    // showMenu(linkUrl, imageUrl, pageUrl) — wird vom Aufrufer auf den UI-Thread marshalled.
    public NovaContextMenuHandler(Action<string, string, string> showMenu)
    {
        _showMenu = showMenu;
    }

    protected override void OnBeforeContextMenu(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        IMenuModel model)
    {
        var link = parameters.LinkUrl ?? "";
        var image = parameters.HasImageContents ? parameters.SourceUrl ?? "" : "";
        var page = parameters.PageUrl ?? "";

        // Natives Menue komplett entfernen.
        model.Clear();

        _showMenu(link, image, page);
    }

    protected override bool RunContextMenu(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        IMenuModel model,
        IRunContextMenuCallback callback)
    {
        // Modell ist leer -> wir rendern unser eigenes WPF-Menue. CEF soll nichts zeigen.
        callback.Cancel();
        return true;
    }
}
