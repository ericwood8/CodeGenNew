using CodeGenNew.App.Services;
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
        UserNameBox.Visibility = ViewModel.UseWindowsAuth ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        PasswordBoxControl.Visibility = UserNameBox.Visibility;

        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
    }

    private void OnAuthModeChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.UseWindowsAuth = WindowsAuthRadio.IsChecked == true;
        var visibility = ViewModel.UseWindowsAuth ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        if (UserNameBox is not null) UserNameBox.Visibility = visibility;
        if (PasswordBoxControl is not null) PasswordBoxControl.Visibility = visibility;
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
