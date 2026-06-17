using CefSharp;
using CefSharp.Handler;

namespace NovaBrowser.App.Services;

public sealed class NovaContextMenuHandler : ContextMenuHandler
{
    private static readonly CefMenuCommand InspectElementCommand = CefMenuCommand.UserFirst;
    private readonly Action<string> _showStatus;

    public NovaContextMenuHandler(Action<string> showStatus)
    {
        _showStatus = showStatus;
    }

    protected override void OnBeforeContextMenu(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        IMenuModel model)
    {
        if (model.Count > 0)
        {
            model.AddSeparator();
        }

        model.AddItem(InspectElementCommand, "Untersuchen");
    }

    protected override bool OnContextMenuCommand(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        CefMenuCommand commandId,
        CefEventFlags eventFlags)
    {
        if (commandId != InspectElementCommand)
        {
            return false;
        }

        browser.GetHost().ShowDevTools(windowInfo: null, inspectElementAtX: parameters.XCoord, inspectElementAtY: parameters.YCoord);
        _showStatus("DevTools geoeffnet.");
        return true;
    }
}
