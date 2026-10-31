namespace CodeGenNew.Core;

/// <summary> The sentences of the members page, the screen where an administrator says which people work in the current tenant and in which role. </summary>
public static class MemberWords
{
    public const string Title = "Members";
    public const string Menu = "Members";
    public const string AddButton = "Add member";
    public const string NoTenant = "Switch to a tenant to manage its members.";
    public const string NobodyToAdd = "Everyone is already a member of this tenant.";
    public const string RoleLabel = "Role";
    public const string RoleHint = "Leave empty to use the person's own role.";
    public const string CannotRemoveSelf = "You cannot remove yourself from the tenant.";
    public const string PersonLabel = "Person";
}

/// <summary> The members page of the WinUI3 app (<c>TenantMembership</c> with the desktop sign-in): the people of the current tenant with their role, Add member, Edit role and Remove. The membership table has a
/// role column besides its two keys, so the two-list junction editor does not fit it. The page is in the menu for administrators; it reads the membership and the user table by hand with the query filters off,
/// since a person of another tenant is not visible to the tenant's own filter, and always names the current tenant. </summary>
public static class DesktopMembersCode
{
    /// <summary> Whether the app has a members page: it signs in, works in tenants and a membership table says who is in which. </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        TenantPlan.HasDesktopSwitcher(project) && TenantPlan.TenantTableOf(database, project) is not null
        && TenantPlan.MembershipOf(database, project) is not null && UserTable.Resolve(database, project).Plan is not null;

    public const string PageName = "MembersPage";

    /// <summary> The columns of the user table that name a person for a list, in the order they are looked for. </summary>
    private static readonly string[] NameColumns = ["Name", "FullName", "DisplayName", "PersonName", "EmployeeName", "Title"];

    public static string Xaml(string viewNamespace, bool hasRole) => Fill(XamlText, ("§NS", viewNamespace),
        ("§ROLECOLUMN", hasRole ? RoleColumnXaml : ""), ("§ROLECELL", hasRole ? RoleCellXaml : ""), ("§ACTIONCOLUMN", hasRole ? "2" : "1"), ("§ROLEBUTTON", hasRole ? "<Button Content=\"Edit role\" Tag=\"{x:Bind PersonId}\" Click=\"OnEditRoleClick\" />" : ""),
        ("§ROLEGRIDCOLUMN", hasRole ? "<ColumnDefinition Width=\"200\" />" : ""));

    public static string Code(DatabaseModel database, ProjectSettings project, string viewNamespace, string contextType, IEnumerable<string> usings)
    {
        var user = UserTable.Resolve(database, project).Plan!;
        var membership = TenantPlan.MembershipOf(database, project)!;
        var userKey = user.Table.PrimaryKeyColumns[0];
        var nameColumn = user.Table.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && !c.IsCredentialColumn && !c.Name.EqualsIgnoreCase(user.UserName.Name) && NameColumns.Any(n => n.EqualsIgnoreCase(c.Name)));
        string personText = nameColumn is null
            ? $"(t.{user.UserName.CSharpName()} ?? \"\")"
            : $"({(nameColumn.IsNullable ? $"t.{nameColumn.CSharpName()} ?? \"\"" : $"t.{nameColumn.CSharpName()}")}) + \" (\" + t.{user.UserName.CSharpName()} + \")\"";
        return Fill(CodeText, ("§NS", viewNamespace), ("§CTX", contextType), ("§USINGS", string.Concat(usings.Select(u => $"using {u};\n"))),
            ("§U", user.Table.TableName), ("§UKEY", userKey.CSharpName()), ("§ULOGIN", user.UserName.CSharpName()), ("§PERSONTEXT", personText),
            ("§MROLEVALUE", membership.Role is null ? "(string?)null" : $"m.{membership.Role.CSharpName()}"),
            ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
            ("§ROLEEDIT", membership.Role is null ? "" : Fill(RoleEditText, ("§M", membership.Table.TableName), ("§MUSER", membership.User.CSharpName()), ("§MTENANT", membership.Tenant.CSharpName()),
                ("§MROLE", membership.Role.CSharpName()), ("§ROLELABEL", MemberWords.RoleLabel), ("§ROLEHINT", MemberWords.RoleHint))),
            ("§ROLEASSIGN", membership.Role is null ? "" : $", {membership.Role.CSharpName()} = role"),
            ("§ROLEREAD", membership.Role is null ? "" : "\n        string? role = string.IsNullOrWhiteSpace(roleBox.Text) ? null : roleBox.Text.Trim();"),
            ("§ROLEBOX", membership.Role is null ? "" : "\n        var roleBox = new TextBox { Header = \"" + MemberWords.RoleLabel + "\", PlaceholderText = \"" + MemberWords.RoleHint + "\" };"),
            ("§ROLEPANEL", membership.Role is null ? "" : ", roleBox"),
            ("§NOTENANT", MemberWords.NoTenant), ("§NOBODY", MemberWords.NobodyToAdd), ("§SELF", MemberWords.CannotRemoveSelf), ("§PERSONLABEL", MemberWords.PersonLabel));
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string RoleColumnXaml = "<TextBlock Grid.Column=\"1\" Text=\"Role\" FontWeight=\"SemiBold\" />";

    private const string RoleCellXaml = "<TextBlock Grid.Column=\"1\" Text=\"{x:Bind Role}\" VerticalAlignment=\"Center\" />";

    private const string XamlText = """
<Page
    x:Class="§NS.MembersPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:§NS">

    <Grid Padding="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Members" Style="{ThemeResource TitleTextBlockStyle}" />

        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8">
            <Button x:Name="AddButton" Content="Add member" Click="OnAddClick" />
            <Button Content="Refresh" Click="OnRefreshClick" />
        </StackPanel>

        <InfoBar x:Name="Problem" Grid.Row="2" Severity="Error" IsOpen="False" IsClosable="False" />

        <ListView Grid.Row="3" ItemsSource="{x:Bind Rows}" SelectionMode="None">
            <ListView.Header>
                <Grid ColumnSpacing="8" Padding="12,0,12,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        §ROLEGRIDCOLUMN
                        <ColumnDefinition Width="200" />
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column="0" Text="Person" FontWeight="SemiBold" />
                    §ROLECOLUMN
                </Grid>
            </ListView.Header>
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="local:MemberRow">
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            §ROLEGRIDCOLUMN
                            <ColumnDefinition Width="200" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{x:Bind Person}" VerticalAlignment="Center" />
                        §ROLECELL
                        <StackPanel Grid.Column="§ACTIONCOLUMN" Orientation="Horizontal" Spacing="8">
                            §ROLEBUTTON
                            <Button Content="Remove" Tag="{x:Bind PersonId}" Click="OnRemoveClick" />
                        </StackPanel>
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
""";

    private const string CodeText = """
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
§USINGS
namespace §NS;

/// <summary> One member of the current tenant: the key and the sign-in name of the person, the text that names them and their role in the tenant. </summary>
public sealed class MemberRow
{
    public MemberRow(int personId, string login, string person, string? role)
    {
        PersonId = personId;
        Login = login;
        Person = person;
        Role = role;
    }

    public int PersonId { get; set; }
    public string Login { get; set; }
    public string Person { get; set; }
    public string? Role { get; set; }
}

/// <summary> A person who is not yet a member of the current tenant. </summary>
public sealed record MemberCandidate(int Id, string Name);

// A plain Page: the menu shows it to administrators. Add member, Edit role and Remove change the membership rows of the current tenant.
public sealed partial class MembersPage : Page
{
    private readonly §CTX _context;

    public ObservableCollection<MemberRow> Rows { get; } = [];

    public MembersPage(§CTX context)
    {
        _context = context;
        InitializeComponent();
        AddButton.IsEnabled = _context.CurrentTenantId is not null;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        Rows.Clear();
        if (_context.CurrentTenantId is not int tenant)
        {
            Show("§NOTENANT");
            return;
        }
        try
        {
            var rows = await _context.Set<§M>().IgnoreQueryFilters().AsNoTracking().Where(m => m.§MTENANT == tenant)
                .Join(_context.Set<§U>().IgnoreQueryFilters().AsNoTracking(), m => m.§MUSER, t => t.§UKEY,
                    (m, t) => new { Id = t.§UKEY, Login = t.§ULOGIN, Text = §PERSONTEXT, Role = §MROLEVALUE })
                .ToListAsync();
            foreach (var r in rows.OrderBy(r => r.Text, StringComparer.CurrentCultureIgnoreCase))
                Rows.Add(new MemberRow(r.Id, r.Login ?? "", r.Text, r.Role));
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            Show(ex.Message);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_context.CurrentTenantId is not int tenant)
            return;
        List<MemberCandidate> candidates;
        try
        {
            var memberIds = await _context.Set<§M>().IgnoreQueryFilters().AsNoTracking().Where(m => m.§MTENANT == tenant).Select(m => m.§MUSER).ToListAsync();
            var people = await _context.Set<§U>().IgnoreQueryFilters().AsNoTracking().Where(t => !memberIds.Contains(t.§UKEY))
                .Select(t => new { Id = t.§UKEY, Text = §PERSONTEXT }).ToListAsync();
            candidates = people.OrderBy(p => p.Text, StringComparer.CurrentCultureIgnoreCase).Select(p => new MemberCandidate(p.Id, p.Text)).ToList();
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            Show(ex.Message);
            return;
        }
        if (candidates.Count == 0)
        {
            Show("§NOBODY");
            return;
        }
        var picker = new ComboBox { Header = "§PERSONLABEL", ItemsSource = candidates, DisplayMemberPath = "Name", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };§ROLEBOX
        var dialog = new ContentDialog
        {
            Title = "Add member",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Spacing = 12, MinWidth = 320, Children = { picker§ROLEPANEL } }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedItem is not MemberCandidate chosen)
            return;§ROLEREAD
        try
        {
            _context.Set<§M>().Add(new §M { §MUSER = chosen.Id, §MTENANT = tenant§ROLEASSIGN });
            await _context.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _context.ChangeTracker.Clear();
            Show(ex.Message);
            return;
        }
        await LoadAsync();
    }
§ROLEEDIT
    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_context.CurrentTenantId is not int tenant || (sender as FrameworkElement)?.Tag is not int personId)
            return;
        var row = Rows.FirstOrDefault(r => r.PersonId == personId);
        if (row is not null && string.Equals(row.Login, DesktopSession.User?.Name, StringComparison.OrdinalIgnoreCase))
        {
            Show("§SELF");
            return;
        }
        try
        {
            var membership = await _context.Set<§M>().IgnoreQueryFilters().FirstOrDefaultAsync(m => m.§MUSER == personId && m.§MTENANT == tenant);
            if (membership is not null)
            {
                _context.Set<§M>().Remove(membership);
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _context.ChangeTracker.Clear();
            Show(ex.Message);
            return;
        }
        await LoadAsync();
    }

    private void Show(string message)
    {
        Problem.Message = message;
        Problem.IsOpen = true;
    }
}
""";

    private const string RoleEditText = """

    private async void OnEditRoleClick(object sender, RoutedEventArgs e)
    {
        if (_context.CurrentTenantId is not int tenant || (sender as FrameworkElement)?.Tag is not int personId)
            return;
        var row = Rows.FirstOrDefault(r => r.PersonId == personId);
        var box = new TextBox { Header = "§ROLELABEL", Text = row?.Role ?? "", PlaceholderText = "§ROLEHINT" };
        var dialog = new ContentDialog
        {
            Title = "Edit role",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Spacing = 12, MinWidth = 320, Children = { box } }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        try
        {
            var membership = await _context.Set<§M>().IgnoreQueryFilters().FirstOrDefaultAsync(m => m.§MUSER == personId && m.§MTENANT == tenant);
            if (membership is not null)
            {
                membership.§MROLE = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
                await _context.SaveChangesAsync();
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _context.ChangeTracker.Clear();
            Show(ex.Message);
            return;
        }
        await LoadAsync();
    }
""";
}
