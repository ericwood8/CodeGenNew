using CodeGenNew.Connections;
using CodeGenNew.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace CodeGenNew.App.ViewModels;

/// <summary> Backs the Connection screen (Docs/specs.md section 9.1). Password is held only in memory
/// for the lifetime of this dialog/session -- never written to Settings.json. </summary>
public partial class ConnectionDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _serverName = "";

    [ObservableProperty]
    private string _databaseName = "";

    [ObservableProperty]
    private bool _useWindowsAuth = true;

    [ObservableProperty]
    private string _userName = "";

    /// <summary> Set directly by the View's PasswordBox.PasswordChanged handler -- PasswordBox doesn't
    /// support two-way XAML binding to its Password property. </summary>
    public string Password { get; set; } = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    [ObservableProperty]
    private bool _isBusy;

    public bool LastTestSucceeded { get; private set; }

    public ConnectionRequest BuildRequest() => new()
    {
        Provider = DatabaseProvider.SqlServer,
        ServerName = ServerName.Trim(),
        DatabaseName = DatabaseName.Trim(),
        AuthMode = UseWindowsAuth ? AuthMode.WindowsAuth : AuthMode.SqlLogin,
        UserName = UseWindowsAuth ? null : UserName.Trim(),
        Password = UseWindowsAuth ? null : Password
    };

    public void LoadFromSettings(LastConnectionSettings last)
    {
        ServerName = last.ServerName;
        DatabaseName = last.DatabaseName;
        UseWindowsAuth = last.AuthMode == "WindowsAuth";
        UserName = last.UserName;
    }

    public void SaveToSettings(LastConnectionSettings last)
    {
        last.ServerName = ServerName.Trim();
        last.DatabaseName = DatabaseName.Trim();
        last.AuthMode = UseWindowsAuth ? "WindowsAuth" : "SqlLogin";
        last.UserName = UseWindowsAuth ? "" : UserName.Trim();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerName) || string.IsNullOrWhiteSpace(DatabaseName))
        {
            StatusMessage = "Server and database are required.";
            StatusSeverity = InfoBarSeverity.Error;
            LastTestSucceeded = false;
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing connection...";
        StatusSeverity = InfoBarSeverity.Informational;
        try
        {
            bool success = await SqlServerConnectionFactory.TestConnectionAsync(BuildRequest());
            LastTestSucceeded = success;
            StatusMessage = success ? "Connection succeeded." : "Connection failed -- check the details and try again.";
            StatusSeverity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        }
        catch (Exception ex)
        {
            LastTestSucceeded = false;
            StatusMessage = $"Connection failed: {ex.Message}";
            StatusSeverity = InfoBarSeverity.Error;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
