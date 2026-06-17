using CefSharp;

namespace NovaBrowser.App.Services;

public sealed class NovaJsDialogHandler : IJsDialogHandler
{
    private readonly Action<string> _showStatus;

    public NovaJsDialogHandler(Action<string> showStatus)
    {
        _showStatus = showStatus;
    }

    public bool OnJSDialog(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        string originUrl,
        CefJsDialogType dialogType,
        string messageText,
        string defaultPromptText,
        IJsDialogCallback callback,
        ref bool suppressMessage)
    {
        if (IsUnityAudioDecodeError(messageText))
        {
            suppressMessage = true;
            callback.Continue(true);
            _showStatus("Diese Unity-Seite nutzt ein Audioformat, das der eingebettete Browser nicht decodieren konnte. Die Seite laeuft weiter, Audio kann fehlen.");
            return true;
        }

        return false;
    }

    public bool OnBeforeUnloadDialog(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        string messageText,
        bool isReload,
        IJsDialogCallback callback)
    {
        return false;
    }

    public void OnResetDialogState(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    public void OnDialogClosed(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
    }

    private static bool IsUnityAudioDecodeError(string messageText)
    {
        return messageText.Contains("EncodingError", StringComparison.OrdinalIgnoreCase) &&
               messageText.Contains("Unable to decode audio data", StringComparison.OrdinalIgnoreCase);
    }
}
