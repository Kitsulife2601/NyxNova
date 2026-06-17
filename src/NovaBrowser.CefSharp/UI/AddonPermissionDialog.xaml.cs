using System.Windows;
using NovaBrowser.App.Models;

namespace NovaBrowser.App.UI;

public partial class AddonPermissionDialog : Window
{
    public AddonPermissionDialog(AddonItem addon)
    {
        InitializeComponent();
        DialogTitleText.Text = $"{addon.Name} moechte Zugriff";
        PermissionsList.ItemsSource = addon.Permissions.Concat(addon.HostPermissions.Select(host => $"Zugriff auf {host}")).ToList();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
