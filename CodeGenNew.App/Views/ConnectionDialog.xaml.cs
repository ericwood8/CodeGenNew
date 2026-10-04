using CodeGenNew.App.Services;
using CodeGenNew.Connections;
using CodeGenNew.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace CodeGenNew.App.Views;

public sealed partial class ConnectionDialog : ContentDialog
{
    public ConnectionDialogViewModel ViewModel { get; }

    public ConnectionDialog(ConnectionDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ContentDialogUx.Apply(this, primaryAccessKey: "S", secondaryAccessKey: "T", closeAccessKey: "C");

        WindowsAuthRadio.IsChecked = ViewModel.UseWindowsAuth;
        SqlLoginRadio.IsChecked = !ViewModel.UseWindowsAuth;
        ProviderCombo.SelectedIndex = ViewModel.Provider switch { DatabaseProvider.PostgreSql => 1, DatabaseProvider.MySql => 2, DatabaseProvider.Sqlite => 3, _ => 0 };
        UpdateAuthVisibility();

        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
    }

    private void OnAuthModeChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.UseWindowsAuth = WindowsAuthRadio.IsChecked == true;
        UpdateAuthVisibility();
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.Provider = ProviderCombo.SelectedIndex switch { 1 => DatabaseProvider.PostgreSql, 2 => DatabaseProvider.MySql, 3 => DatabaseProvider.Sqlite, _ => DatabaseProvider.SqlServer };
        bool isFile = ViewModel.Provider == DatabaseProvider.Sqlite;
        ServerBox.Visibility = isFile ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        DatabaseBox.Header = isFile ? "Database file (the path of the .db file)" : "Database";
        ServerBox.PlaceholderText = ViewModel.Provider switch
        {
            DatabaseProvider.PostgreSql => "host or host:port (default port 5432)",
            DatabaseProvider.MySql => "host or host:port (default port 3306)",
            DatabaseProvider.Sqlite => "",
            _ => ""
        };
        UpdateAuthVisibility();
    }

    /// <summary> SQL Server offers Windows or SQL login; PostgreSQL and MySQL always use a user name and password. </summary>
    private void UpdateAuthVisibility()
    {
        if (UserNameBox is null || PasswordBoxControl is null || AuthRadios is null)
            return;

        AuthRadios.Visibility = ViewModel.Provider != DatabaseProvider.SqlServer ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        var credentials = ViewModel.UsesWindowsAuth || ViewModel.Provider == DatabaseProvider.Sqlite ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        UserNameBox.Visibility = credentials;
        PasswordBoxControl.Visibility = credentials;
    }

    private void OnPasswordChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBoxControl.Password;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.TestConnectionCommand.ExecuteAsync(null);
            if (!ViewModel.LastTestSucceeded)
                args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = true; // "Test" never closes the dialog
            await ViewModel.TestConnectionCommand.ExecuteAsync(null);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
