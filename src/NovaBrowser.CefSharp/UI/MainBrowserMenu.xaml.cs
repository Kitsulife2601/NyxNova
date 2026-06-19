using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NovaBrowser.App.UI;

public partial class MainBrowserMenu : UserControl
{
    private Popup? _submenuPopup;

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
    public event EventHandler? StoreRequested;
    public event EventHandler? DevToolsRequested;
    public event EventHandler? DiagnosticsRequested;
    public event EventHandler? MediaDiagnosticsRequested;
    public event EventHandler? SavePageRequested;
    public event EventHandler? CopyLinkRequested;
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

    private void HistorySubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(HistoryMenuButton,
            ("Verlauf oeffnen", "\uE81C", HistoryRequested),
            ("Zuletzt geschlossene Tabs", "\uE81C", HistoryRequested),
            ("Vollstaendige Verlaufsansicht", "\uE8A7", HistoryRequested),
            ("Browserdaten loeschen", "\uE74D", ClearDataRequested));
    }

    private void BookmarksSubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(BookmarksMenuButton,
            ("Lesezeichen anzeigen", "\uE734", BookmarksRequested),
            ("Aktuelle Seite merken", "\uE735", BookmarksRequested),
            ("Lesezeichen-Leiste", "\uE8A9", BookmarksRequested));
    }

    private void TabGroupsSubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(TabGroupsMenuButton,
            ("Neue Tabgruppe", "\uE8A9", TabGroupsRequested),
            ("Aktuellen Tab gruppieren", "\uE8A9", TabGroupsRequested),
            ("Tabgruppen verwalten", "\uE713", TabGroupsRequested));
    }

    private void ExtensionsSubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(ExtensionsMenuButton,
            ("Nova Addons verwalten", "\uECAA", ExtensionsRequested),
            ("NovaStore oeffnen", "\uE719", StoreRequested),
            ("Angeheftete Addons", "\uE718", ExtensionsRequested));
    }

    private void FindEditSubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(FindEditMenuButton,
            ("Auf Seite suchen", "\uE721", FindEditRequested),
            ("Adresse kopieren", "\uE8C8", CopyLinkRequested),
            ("Seite neu laden", "\uE72C", null));
    }

    private void ShareSubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(ShareMenuButton,
            ("Link kopieren", "\uE8C8", CopyLinkRequested),
            ("Seite speichern", "\uE74E", SavePageRequested),
            ("Drucken", "\uE749", PrintRequested));
    }

    private void MoreToolsSubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(MoreToolsMenuButton,
            ("DevTools oeffnen", "\uE943", DevToolsRequested),
            ("Diagnose oeffnen", "\uE9D9", DiagnosticsRequested),
            ("Media-Diagnose", "\uE768", MediaDiagnosticsRequested),
            ("Seite speichern", "\uE74E", SavePageRequested),
            ("Task Manager", "\uE9D9", MoreToolsRequested));
    }

    private void HelpSubmenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSubmenu(HelpMenuButton,
            ("NyxNova Hilfe", "\uE897", HelpRequested),
            ("Update pruefen", "\uE895", DiagnosticsRequested),
            ("Info zu NyxNova", "\uE946", SettingsRequested));
    }

    private void ShowSubmenu(Button anchor, params (string Text, string Icon, EventHandler? Action)[] items)
    {
        if (_submenuPopup is { IsOpen: true, PlacementTarget: Button current } && ReferenceEquals(current, anchor))
        {
            _submenuPopup.IsOpen = false;
            return;
        }

        _submenuPopup ??= CreateSubmenuPopup();
        _submenuPopup.PlacementTarget = anchor;
        _submenuPopup.Child = BuildSubmenu(items);
        _submenuPopup.IsOpen = true;
    }

    private Popup CreateSubmenuPopup()
    {
        return new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Right,
            HorizontalOffset = 6,
            VerticalOffset = -8,
            StaysOpen = false
        };
    }

    private Border BuildSubmenu((string Text, string Icon, EventHandler? Action)[] items)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 7, 0, 7) };

        foreach (var item in items)
        {
            panel.Children.Add(CreateSubmenuButton(item.Text, item.Icon, item.Action));
        }

        var border = new Border
        {
            Width = 238,
            Background = CreateBrush("#18101f", "#0a060d"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(159, 77, 255)),
            BorderThickness = new Thickness(1.1),
            CornerRadius = new CornerRadius(15),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 0,
                Color = Color.FromRgb(143, 66, 255),
                Opacity = 0.34
            }
        };

        return border;
    }

    private Button CreateSubmenuButton(string text, string icon, EventHandler? action)
    {
        var iconBlock = new TextBlock
        {
            Text = icon,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Foreground = new SolidColorBrush(Color.FromRgb(255, 47, 136)),
            FontSize = 13,
            Width = 25,
            VerticalAlignment = VerticalAlignment.Center
        };

        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 239, 255)),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dock = new DockPanel();
        dock.Children.Add(iconBlock);
        dock.Children.Add(textBlock);

        var button = new Button
        {
            Content = dock,
            MinHeight = 28,
            Margin = new Thickness(8, 1, 8, 1),
            Padding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null
        };

        button.Template = CreateSubmenuButtonTemplate();
        button.Click += (_, _) =>
        {
            _submenuPopup?.SetCurrentValue(Popup.IsOpenProperty, false);
            action?.Invoke(this, EventArgs.Empty);
        };

        return button;
    }

    private static ControlTemplate CreateSubmenuButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "RowRoot";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(MarginProperty, new Thickness(0));
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, CreateBrush("#3a164d", "#241130"), "RowRoot"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(138, 57, 255)), "RowRoot"));
        template.Triggers.Add(hover);

        return template;
    }

    private static LinearGradientBrush CreateBrush(string start, string end)
    {
        return new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(start),
            (Color)ColorConverter.ConvertFromString(end),
            new Point(0, 0),
            new Point(1, 1));
    }
}
