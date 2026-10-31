using System.Text;

namespace CodeGenNew.Core;

/// <summary> The sentences of the WinUI3 sign-in, the same as the Uno sign-in page shows. </summary>
public static class DesktopSignInWords
{
    public const string WrongPassword = "The user name or password is not right.";
    public const string TooManyTries = "Too many tries. Wait a minute and try again.";
    public const string Unavailable = "Could not sign in right now.";
}

/// <summary> The C# of the WinUI3 sign-in (<c>Auth</c> with the WinUI3 stack and a user table). The app reads the database itself, so it checks the password against the user table with the hash the API writes
/// (<c>PasswordService</c>), picks the tenant the person works in and gives the context its <c>ITenantScope</c>. The text uses <c>§</c> tokens so the code reads as code. </summary>
public static class DesktopSignInCode
{
    public static string Session(DatabaseModel database, ProjectSettings project, string contextType, string[] usings, string ns)
    {
        var (plan, _) = UserTable.Resolve(database, project);
        if (plan is null)
            return "";
        var tenantTable = TenantPlan.HasDesktopTenants(project) ? TenantPlan.TenantTableOf(database, project) : null;
        var tenantKey = tenantTable is null ? null : TenantPlan.KeyOf(tenantTable);
        var membership = tenantKey is null ? null : TenantPlan.MembershipOf(database, project);
        var userOwned = tenantKey is null ? null : TenantPlan.OwnedBy(plan.Table, project);
        bool tenants = tenantKey is not null && (membership is not null || userOwned is not null);
        var active = tenantTable?.IsActiveColumn;

        string roleExpression = plan.Role == null ? "null"
            : plan.RoleIsFlag ? $"row.Role == true ? {CSharpText.Quote(plan.RoleWhenTrue)} : {CSharpText.Quote(plan.RoleWhenFalse)}"
            : "row.Role";
        if (plan.Admin != null)
            roleExpression = $"row.Admin == true ? {CSharpText.Quote(project.AuthAdminRole)} : ({roleExpression})";
        string ownerExpression = plan.Owner == null ? "null" : plan.Owner.IsNullable ? "row.Owner" : "(int?)row.Owner";

        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append($"using System.Security.Cryptography;\nusing Microsoft.EntityFrameworkCore;\n\nnamespace {ns};\n\n");
        string platform = $"\n{{\n    /// <summary> A platform administrator: the administrator role, working in no tenant. </summary>\n    public bool IsPlatform => TenantId is null && Role == {CSharpText.Quote(project.AuthAdminRole)};\n}}";
        o.Append(Fill(User, ("§TENANTDOC", tenants ? ", the tenant they work in and their own role (a membership of the tenant may replace the role)" : ""),
            ("§TENANT", tenants ? ", int? TenantId, string? OwnRole" : ""), ("§BODY", tenants ? platform : ";")));
        if (TenantPlan.HasDesktopRoles(project))
            o.Append(SessionText);
        string tenantLines = "";
        if (tenants)
        {
            string activeJoin = active is null ? "" : $".Where(t => t.{active!.CSharpName()})";
            o.Append(Fill(Choice));
            string memberText = membership is not null
                ? Fill(Members, ("§CTX", contextType), ("§U", plan.Table.TableName), ("§UNAME", plan.UserName.CSharpName()),
                    ("§UKEY", plan.Table.PrimaryKeyColumns[0].CSharpName()), ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
                    ("§MROLE", membership.Role is null ? "(string?)null" : $"m.{membership.Role.CSharpName()}"), ("§TT", tenantTable!.TableName), ("§TKEY", tenantKey!.CSharpName()),
                    ("§ACTIVEJOIN", activeJoin), ("§TEXT", TenantPlan.TextExpression(tenantTable!, tenantKey!)),
                    ("§SOFTWHERE", TenantPlan.DeletedFlagOf(tenantTable!) is { } deleted ? $".Where(t => !t.{deleted.CSharpName()})" : ""))
                : "";
            tenantLines = membership is not null ? SignInMember : SignInOwn;
            o.Append(Fill(Problems));
            o.Append(Fill(SignIn, ("§CTX", contextType), ("§U", plan.Table.TableName), ("§UNAME", plan.UserName.CSharpName()), ("§UHASH", plan.PasswordHash.CSharpName()),
                                ("§UROLE", plan.Role == null ? "(string?)null" : "u." + plan.Role.CSharpName()), ("§UADMIN", plan.Admin == null ? "" : $"                Admin = u.{plan.Admin.CSharpName()},\n"),
                ("§UOWNER", plan.Owner == null ? "(int?)null" : "u." + plan.Owner.CSharpName()), ("§ROLE", roleExpression), ("§OWNER", ownerExpression),
                ("§TENANTLINES", tenantLines), ("§TENANTSELECT", userOwned is null || membership is not null ? "" : Fill(OwnSelect, ("§UTENANT", userOwned.Column.CSharpName()),
                    ("§ACTIVETEST", active is null ? "" : $" && context.Set<{tenantTable!.TableName}>().IgnoreQueryFilters().Any(t => t.{tenantKey!.CSharpName()} == u.{userOwned.Column.CSharpName()} && t.{active.CSharpName()})"))),
                ("§MEMBERS", memberText), ("§SWITCH", membership is null ? "" : SwitchText), ("§TENANTRESULT", ", tenant, ownRole"), ("§TENANTPARAM", ", int? tenantId = null")));
        }
        else
        {
            o.Append(Fill(Problems));
            o.Append(Fill(SignIn, ("§CTX", contextType), ("§U", plan.Table.TableName), ("§UNAME", plan.UserName.CSharpName()), ("§UHASH", plan.PasswordHash.CSharpName()),
                ("§UROLE", plan.Role == null ? "(string?)null" : "u." + plan.Role.CSharpName()), ("§UADMIN", plan.Admin == null ? "" : $"                Admin = u.{plan.Admin.CSharpName()},\n"),
                ("§UOWNER", plan.Owner == null ? "(int?)null" : "u." + plan.Owner.CSharpName()), ("§ROLE", roleExpression), ("§OWNER", ownerExpression),
                ("§TENANTLINES", ""), ("§TENANTSELECT", ""), ("§MEMBERS", ""), ("§SWITCH", ""), ("§TENANTRESULT", ""), ("§TENANTPARAM", "")));
        }
        return o.ToString();
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    /// <summary> The sign-in view. <paramref name="dropdown"/>: the person picks the tenant by name (a membership table is named); a person whose tenant is a column of their own row has no choice to make. </summary>
    public static string LoginXaml(string viewNamespace, bool dropdown) =>
        Fill(LoginXamlText, ("§NS", viewNamespace),
            ("§TENANTBOX", dropdown ? $"            <ComboBox x:Name=\"TenantBox\" Header=\"{TenantWords.SignInLabel}\" DisplayMemberPath=\"Name\" HorizontalAlignment=\"Stretch\" />\n            <TextBlock Text=\"{TenantWords.SignInHint}\" Style=\"{{ThemeResource CaptionTextBlockStyle}}\" Opacity=\"0.7\" TextWrapping=\"Wrap\" />\n" : ""));

    public static string LoginCode(string viewNamespace, string servicesNamespace, string contextType, bool tenants, bool dropdown) =>
        Fill(LoginCodeText, ("§NS", viewNamespace), ("§SERVICES", servicesNamespace), ("§CTX", contextType),
            ("§TENANTARG", dropdown ? ", (TenantBox.SelectedItem as SignInTenant)?.Id" : ""),
            ("§TENANTLOAD", dropdown ? Fill(TenantLoad, ("§CTX", contextType)) : ""),
            ("§LOADED", dropdown ? "\n        Loaded += async (_, _) => await LoadTenantsAsync();" : ""),
            ("§NOTMEMBER", tenants ? $"                SignInProblem.NotMember => \"{TenantWords.SignInRefused}\",\n" : ""),
            ("§TOOMANY", DesktopSignInWords.TooManyTries), ("§WRONG", DesktopSignInWords.WrongPassword), ("§UNAVAILABLE", DesktopSignInWords.Unavailable));

    private const string User = """
/// <summary> Who is signed in: the name, the role and the owner id the user table gives§TENANTDOC. </summary>
public sealed record SignedInUser(string Name, string? Role, int? OwnerId§TENANT)§BODY


""";

    private const string SessionText = """
/// <summary> The person signed in to the app. The screens ask for their role: the menu lists the screens the role may read and the buttons that write show to the roles that may write (ReadRoles and WriteRoles). </summary>
public static class DesktopSession
{
    public static SignedInUser? User { get; set; }

    public static bool HasRole(IEnumerable<string> roles) => User?.Role is { } role && roles.Contains(role);
}


""";

    private const string Choice = """
/// <summary> A tenant the person belongs to: the name to show and the role they have in it (null: their own role stays). </summary>
public sealed record TenantChoice(int Id, string Text, string? Role);

/// <summary> One entry of the sign-in's tenant drop-down: the key to sign in to (null: the person's first tenant) and the name to show. </summary>
public sealed record SignInTenant(int? Id, string Name);

""";

    private const string Problems = """
public enum SignInProblem { WrongPassword, TooManyTries, NotMember }

/// <summary> A sign-in that was refused; <see cref="Problem"/> says why. </summary>
public class SignInException(SignInProblem problem) : Exception($"The sign-in was refused: {problem}.")
{
    public SignInProblem Problem => problem;
}

""";

    private const string SignIn = """
/// <summary> Checks a person's password against the user table and works out the tenant they work in. The hash is the one the API writes (<c>pbkdf2-sha256$iterations$salt$hash</c>). </summary>
public static class DesktopSignIn
{
    public const int MaxFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);
    private static readonly Dictionary<string, List<DateTime>> Failures = [];

    public static bool Verify(string password, string stored)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out int iterations) || iterations < 1)
            return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TooMany(string key)
    {
        lock (Failures)
        {
            if (!Failures.TryGetValue(key, out var times))
                return false;
            times.RemoveAll(t => DateTime.UtcNow - t > FailureWindow);
            return times.Count >= MaxFailures;
        }
    }

    private static void Fail(string key)
    {
        lock (Failures)
        {
            if (!Failures.TryGetValue(key, out var times))
                Failures[key] = times = [];
            times.Add(DateTime.UtcNow);
        }
    }

    /// <summary> Forgets the wrong passwords counted so far, so the next sign-in starts clean (the tests of the app use it). </summary>
    public static void ForgetFailures()
    {
        lock (Failures)
            Failures.Clear();
    }

    /// <summary> Signs a person in; a refused sign-in throws <see cref="SignInException"/>. The user table is read without its query filters: nobody works in a tenant yet. </summary>
    public static async Task<SignedInUser> SignInAsync(§CTX context, string userName, string password§TENANTPARAM, CancellationToken cancellationToken = default)
    {
        string name = userName.Trim();
        string key = name.ToLowerInvariant();
        if (TooMany(key))
            throw new SignInException(SignInProblem.TooManyTries);
        var row = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.§UNAME == name && u.§UHASH != null && u.§UHASH != "")
            .Select(u => new
            {
                Name = u.§UNAME,
                Hash = u.§UHASH,
                Role = §UROLE,
§UADMIN§TENANTSELECT                Owner = §UOWNER
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null || !Verify(password, row.Hash!))
        {
            Fail(key);
            throw new SignInException(SignInProblem.WrongPassword);
        }
        lock (Failures)
            Failures.Remove(key);
        string? role = §ROLE;
        int? owner = §OWNER;
§TENANTLINES        return new SignedInUser(row.Name!, role, owner§TENANTRESULT);
    }
§MEMBERS§SWITCH}

""";

    private const string Members = """

    /// <summary> The names for the sign-in's drop-down: the tenants that are not switched off and not marked deleted, by name, after the blank entry that means the person's first tenant. The query filters are ignored: at sign-in there is no tenant yet. </summary>
    public static async Task<List<SignInTenant>> SignInTenantsAsync(§CTX context, CancellationToken cancellationToken = default)
    {
        var rows = await context.Set<§TT>().IgnoreQueryFilters().AsNoTracking()§ACTIVEJOIN§SOFTWHERE
            .Select(t => new { Id = t.§TKEY, Text = §TEXT }).ToListAsync(cancellationToken);
        var list = rows.OrderBy(x => x.Text, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Id).Select(x => new SignInTenant(x.Id, x.Text)).ToList();
        list.Insert(0, new SignInTenant(null, ""));
        return list;
    }

    /// <summary> The active tenants the person belongs to, by tenant id. The query filters are ignored: at sign-in there is no tenant yet. </summary>
    public static async Task<List<TenantChoice>> TenantsAsync(§CTX context, string userName, CancellationToken cancellationToken = default)
    {
        string name = userName.Trim();
        var person = await context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(u => u.§UNAME == name).Select(u => (int?)u.§UKEY).FirstOrDefaultAsync(cancellationToken);
        if (person is null)
            return [];
        var rows = await context.Set<§M>().IgnoreQueryFilters().AsNoTracking().Where(m => m.§MUSER == person)
            .Join(context.Set<§TT>().IgnoreQueryFilters().AsNoTracking()§ACTIVEJOIN, m => m.§MTENANT, t => t.§TKEY, (m, t) => new { Tenant = t.§TKEY, Text = §TEXT, Role = §MROLE })
            .OrderBy(x => x.Tenant).ToListAsync(cancellationToken);
        return rows.Select(x => new TenantChoice(x.Tenant, x.Text, x.Role)).ToList();
    }

""";

    private const string SwitchText = """

    /// <summary> The person in another tenant of theirs; null when they are not a member of it (or it is switched off). </summary>
    public static async Task<SignedInUser?> SwitchAsync(§CTX context, SignedInUser user, int tenantId, CancellationToken cancellationToken = default)
    {
        var members = await TenantsAsync(context, user.Name, cancellationToken);
        return members.FirstOrDefault(m => m.Id == tenantId) is { } chosen ? user with { TenantId = chosen.Id, Role = chosen.Role ?? user.OwnRole } : null;
    }

""";

    private const string SignInMember = """
        string? ownRole = role;
        var members = await TenantsAsync(context, name, cancellationToken);
        int? tenant = null;
        if (members.Count == 0)
        {
            if (tenantId is not null)
                throw new SignInException(SignInProblem.NotMember);
        }
        else
        {
            var chosen = tenantId is { } wanted ? members.FirstOrDefault(m => m.Id == wanted) : members[0];
            if (chosen is null)
                throw new SignInException(SignInProblem.NotMember);
            tenant = chosen.Id;
            role = chosen.Role ?? role;
        }

""";

    private const string OwnSelect = """
                Tenant = (int?)u.§UTENANT,
                TenantOn = true§ACTIVETEST,

""";

    private const string SignInOwn = """
        string? ownRole = role;
        int? tenant = row.Tenant;
        if ((tenant is not null && !row.TenantOn) || (tenantId is not null && tenantId != tenant))
            throw new SignInException(SignInProblem.NotMember);

""";

    private const string TenantLoad = """

    // the names are read when the view is shown; when they cannot be read the drop-down holds only the blank entry, so a person with one tenant can still sign in
    private async Task LoadTenantsAsync()
    {
        try
        {
            using var context = new §CTX();
            TenantBox.ItemsSource = await DesktopSignIn.SignInTenantsAsync(context);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            TenantBox.ItemsSource = new List<SignInTenant> { new(null, "") };
        }
        TenantBox.SelectedIndex = 0;
    }
""";

    private const string LoginXamlText = """
<UserControl
    x:Class="§NS.LoginView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
        <StackPanel Width="320" HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
            <TextBlock Text="Sign in" Style="{ThemeResource TitleTextBlockStyle}" />
            <InfoBar x:Name="Problem" Severity="Error" IsOpen="False" IsClosable="False" />
            <TextBox x:Name="UserNameBox" Header="User name" />
            <PasswordBox x:Name="PasswordBoxField" Header="Password" KeyDown="OnPasswordKeyDown" />
§TENANTBOX            <Button x:Name="SignInButton" Content="Sign in" Style="{ThemeResource AccentButtonStyle}" Click="OnSignInClick" />
        </StackPanel>
    </Grid>
</UserControl>
""";

    private const string LoginCodeText = """
using §SERVICES;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace §NS;

/// <summary> The sign-in the main window shows until someone signs in; <c>signedIn</c> is called with the person. </summary>
public sealed partial class LoginView : UserControl
{
    private readonly Action<SignedInUser> _signedIn;

    public LoginView(Action<SignedInUser> signedIn)
    {
        _signedIn = signedIn;
        InitializeComponent();§LOADED
    }
§TENANTLOAD
    private async void OnSignInClick(object sender, RoutedEventArgs e) => await SignInAsync();

    private async void OnPasswordKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            await SignInAsync();
    }

    private async Task SignInAsync()
    {
        SignInButton.IsEnabled = false;
        Problem.IsOpen = false;
        try
        {
            using var context = new §CTX();
            var user = await DesktopSignIn.SignInAsync(context, UserNameBox.Text, PasswordBoxField.Password§TENANTARG);
            _signedIn(user);
        }
        catch (SignInException ex)
        {
            Show(ex.Problem switch
            {
                SignInProblem.TooManyTries => "§TOOMANY",
§NOTMEMBER                _ => "§WRONG"
            });
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            Show("§UNAVAILABLE");
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void Show(string message)
    {
        Problem.Message = message;
        Problem.IsOpen = true;
        PasswordBoxField.Password = "";
    }
}
""";
}
