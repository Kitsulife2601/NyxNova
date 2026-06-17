using System.Windows;
using System.Windows.Controls;

namespace NovaBrowser.App.UI;

public partial class MainBrowserMenu : UserControl
{
    public static readonly DependencyProperty ZoomTextProperty =
        DependencyProperty.Register(nameof(ZoomText), typeof(string), typeof(MainBrowserMenu), new PropertyMetadata("100 %"));

    public MainBrowserMenu()
    {
        InitializeComponent();
    }

    public string ZoomText
    {
        get => (string)GetValue(ZoomTextProperty);
        set => SetValue(ZoomTextProperty, value);
    }

    public event EventHandler? NewTabRequested;
    public event EventHandler? NewWindowRequested;
    public event EventHandler? IncognitoRequested;
    public event EventHandler? ProfileRequested;
    public event EventHandler? PasswordsRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? DownloadsRequested;
    public event EventHandler? BookmarksRequested;
    public event EventHandler? TabGroupsRequested;
    public event EventHandler? ExtensionsRequested;
    public event EventHandler? ClearDataRequested;
    public event EventHandler? ZoomOutRequested;
    public event EventHandler? ZoomInRequested;
    public event EventHandler? FullscreenRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? LensRequested;
    public event EventHandler? TranslateRequested;
    public event EventHandler? FindEditRequested;
    public event EventHandler? ShareRequested;
    public event EventHandler? MoreToolsRequested;
    public event EventHandler? HelpRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke(this, EventArgs.Empty);
    private void NewWindow_Click(object sender, RoutedEventArgs e) => NewWindowRequested?.Invoke(this, EventArgs.Empty);
    private void Incognito_Click(object sender, RoutedEventArgs e) => IncognitoRequested?.Invoke(this, EventArgs.Empty);
    private void Profile_Click(object sender, RoutedEventArgs e) => ProfileRequested?.Invoke(this, EventArgs.Empty);
    private void Passwords_Click(object sender, RoutedEventArgs e) => PasswordsRequested?.Invoke(this, EventArgs.Empty);
    private void History_Click(object sender, RoutedEventArgs e) => HistoryRequested?.Invoke(this, EventArgs.Empty);
    private void Downloads_Click(object sender, RoutedEventArgs e) => DownloadsRequested?.Invoke(this, EventArgs.Empty);
    private void Bookmarks_Click(object sender, RoutedEventArgs e) => BookmarksRequested?.Invoke(this, EventArgs.Empty);
    private void TabGroups_Click(object sender, RoutedEventArgs e) => TabGroupsRequested?.Invoke(this, EventArgs.Empty);
    private void Extensions_Click(object sender, RoutedEventArgs e) => ExtensionsRequested?.Invoke(this, EventArgs.Empty);
    private void ClearData_Click(object sender, RoutedEventArgs e) => ClearDataRequested?.Invoke(this, EventArgs.Empty);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOutRequested?.Invoke(this, EventArgs.Empty);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomInRequested?.Invoke(this, EventArgs.Empty);
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);
    private void Print_Click(object sender, RoutedEventArgs e) => PrintRequested?.Invoke(this, EventArgs.Empty);
    private void Lens_Click(object sender, RoutedEventArgs e) => LensRequested?.Invoke(this, EventArgs.Empty);
    private void Translate_Click(object sender, RoutedEventArgs e) => TranslateRequested?.Invoke(this, EventArgs.Empty);
    private void FindEdit_Click(object sender, RoutedEventArgs e) => FindEditRequested?.Invoke(this, EventArgs.Empty);
    private void Share_Click(object sender, RoutedEventArgs e) => ShareRequested?.Invoke(this, EventArgs.Empty);
    private void MoreTools_Click(object sender, RoutedEventArgs e) => MoreToolsRequested?.Invoke(this, EventArgs.Empty);
    private void Help_Click(object sender, RoutedEventArgs e) => HelpRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
