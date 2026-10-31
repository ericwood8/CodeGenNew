namespace CodeGenNew.Core;

/// <summary> The sentences of the user table's screen and of the sign-in that creates the first person, the same in every front end. </summary>
public static class UserScreenWords
{
    public const string SetPasswordLabel = "Set password";
    public const string SetPasswordHint = "Leave empty to keep the password.";
    public const string NewPersonNeedsPassword = "A new person needs a password.";
    public const string FirstPersonTitle = "Create the first administrator";
    public const string FirstPersonHint = "Nobody can sign in yet. The user name and password you choose make you the administrator.";
    public const string FirstPersonButton = "Create and sign in";
    public const string ConfirmLabel = "Confirm password";
    public const string PasswordsDiffer = "The two passwords are not the same.";
    public const string UserNameRequired = "Type a user name.";
    public const string FirstPersonTaken = "Someone has already been added. Sign in with that person.";

    public static string TooShort(int minimum) => $"The password needs at least {minimum} characters.";
}

/// <summary> What a screen of the project's user table (<c>UserTable</c>) does beyond an ordinary table screen: the password hash is never shown, a "Set password" box writes a new one, and the administrator flag is
/// shown to administrators only. The rules are written here once so the front ends cannot drift apart. </summary>
public sealed record UserScreenPlan(UserTablePlan User)
{
    /// <summary> The shortest password the "Set password" box and the first person accept (the same as <c>set-password</c>). </summary>
    public const int MinimumPasswordLength = 8;

    /// <summary> The administrator flag column, shown and changed by administrators only; null when the table has none. </summary>
    public ColumnModel? AdminFlag => User.Admin;

    /// <summary> The plan for <paramref name="table"/> when the WinUI3 app signs in and the table is the user table; null otherwise. </summary>
    public static UserScreenPlan? ForDesktop(TableModel table, ProjectSettings project) =>
        TenantPlan.HasDesktopSignIn(project) && UserTable.PlanOf(table, project) is { } plan ? new UserScreenPlan(plan) : null;

    /// <summary> The plan for <paramref name="table"/> when the web app signs in (<c>Auth</c>, <c>ApiAuth</c>) and the table is the user table; null otherwise. </summary>
    public static UserScreenPlan? ForWeb(TableModel table, ProjectSettings project) =>
        project.Auth && project.ApiAuth && UserTable.PlanOf(table, project) is { } plan ? new UserScreenPlan(plan) : null;

    /// <summary> Whether <paramref name="column"/> of the screen's table is shown to administrators only. </summary>
    public bool IsAdminOnly(ColumnModel column) => AdminFlag is { } flag && flag.Name.EqualsIgnoreCase(column.Name);

    /// <summary> The C# expression that is true for an administrator: the signed-in person's role is the administrator role. </summary>
    public static string IsAdministratorExpression(ProjectSettings project) =>
        $"DesktopSession.User?.Role == {CSharpText.Quote(project.AuthAdminRole)}";

    /// <summary> Adds <paramref name="attribute"/> to the first element of a field's XAML (the field of the administrator flag gets its visibility). </summary>
    public static string WithAttribute(string xaml, string attribute)
    {
        int tagStart = xaml.IndexOf('<');
        int space = tagStart < 0 ? -1 : xaml.IndexOfAny([' ', '\n', '/'], tagStart);
        return space < 0 ? xaml : xaml.Insert(space, " " + attribute);
    }

    /// <summary> The "Set password" box of the detail dialog. </summary>
    public static string PasswordFieldXaml() =>
        $"            <PasswordBox x:Name=\"NewPasswordBox\" Header=\"{UserScreenWords.SetPasswordLabel}\" Visibility=\"{{x:Bind ViewModel.PasswordVisibility}}\" Password=\"{{x:Bind ViewModel.NewPassword, Mode=TwoWay}}\" />\n"
        + $"            <TextBlock Text=\"{UserScreenWords.SetPasswordHint}\" Style=\"{{ThemeResource CaptionTextBlockStyle}}\" Opacity=\"0.7\" TextWrapping=\"Wrap\" Visibility=\"{{x:Bind ViewModel.PasswordVisibility}}\" />\n";

    /// <summary> The members the view model of the detail dialog gains: the new password, who may set it and who sees the administrator flag. </summary>
    public string ViewModelMembers(ProjectSettings project) => $$"""

    /// <summary> What was typed in the Set password box; empty keeps the password the person has. </summary>
    [ObservableProperty]
    private string _newPassword = "";

    private static bool SignedInIsAdministrator() => {{IsAdministratorExpression(project)}};

    /// <summary> An administrator sets anyone's password; anyone else only their own. </summary>
    private bool MaySetPassword => SignedInIsAdministrator() || (_editing is not null && string.Equals(_editing.{{User.UserName.CSharpName()}}, DesktopSession.User?.Name, System.StringComparison.OrdinalIgnoreCase));

    public Microsoft.UI.Xaml.Visibility PasswordVisibility => MaySetPassword ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility AdminOnlyVisibility => SignedInIsAdministrator() ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

""";

    /// <summary> The members the Uno view model of the detail dialog gains: the same as <see cref="ViewModelMembers"/>, with the signed-in person read from the app's <c>ApiClients</c>. </summary>
    public string UnoViewModelMembers(ProjectSettings project) => $$"""

    /// <summary> What was typed in the Set password box; empty keeps the password the person has. </summary>
    [ObservableProperty]
    private string _newPassword = "";

    private bool SignedInIsAdministrator() => _api.Session.HasRole([{{CSharpText.Quote(project.AuthAdminRole)}}]);

    /// <summary> An administrator sets anyone's password; anyone else only their own. </summary>
    private bool MaySetPassword => SignedInIsAdministrator() || (_editing is not null && string.Equals(_editing.{{User.UserName.CSharpName()}}, _api.Session.User?.Name, System.StringComparison.OrdinalIgnoreCase));

    public Microsoft.UI.Xaml.Visibility PasswordVisibility => MaySetPassword ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility AdminOnlyVisibility => SignedInIsAdministrator() ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

""";

    /// <summary> The lines of <c>SaveAsync</c> that refuse a password that is too short, or missing for a new person (the Uno app sends the password to the API after the row is saved). </summary>
    public static string PasswordCheckLines() => $$"""
        if (MaySetPassword)
        {
            if (NewPassword.Length > 0 && NewPassword.Length < {{MinimumPasswordLength}})
            {
                ErrorMessage = "{{UserScreenWords.TooShort(MinimumPasswordLength)}}";
                return false;
            }
            if (NewPassword.Length == 0 && _editing is null)
            {
                ErrorMessage = "{{UserScreenWords.NewPersonNeedsPassword}}";
                return false;
            }
        }

""";

    /// <summary> The Uno line that sends the typed password once the row is saved. </summary>
    public string UnoPasswordCall() => $"            if (MaySetPassword && NewPassword.Length > 0)\n                await PeopleApi.ChangePasswordAsync(_api.Http, entity.{User.UserName.CSharpName()} ?? \"\", NewPassword);\n";

    /// <summary> The plan for <paramref name="table"/> when the Uno app signs in (<c>Auth</c>, <c>ApiAuth</c>) and the table is the user table; null otherwise. </summary>
    public static UserScreenPlan? ForUno(TableModel table, ProjectSettings project) => ForWeb(table, project);

    /// <summary> The lines of <c>SaveAsync</c> that hash the typed password into the entity. </summary>
    public string PasswordSaveLines() => $$"""
        if (MaySetPassword)
        {
            if (!string.IsNullOrEmpty(NewPassword))
            {
                if (NewPassword.Length < DesktopSignIn.MinimumPasswordLength)
                {
                    ErrorMessage = DesktopSignIn.PasswordTooShort;
                    return false;
                }
                entity.{{User.PasswordHash.CSharpName()}} = DesktopSignIn.Hash(NewPassword);
            }
            else if (_editing is null)
            {
                ErrorMessage = "{{UserScreenWords.NewPersonNeedsPassword}}";
                return false;
            }
        }

""";

    /// <summary> Wraps the save lines of the administrator flag so only an administrator changes it. </summary>
    public static string AdminOnlySaveLines(string lines) =>
        "        if (SignedInIsAdministrator())\n        {\n" + string.Concat(lines.TrimEnd('\n').Split('\n').Select(l => "    " + l + "\n")) + "        }\n";
}
