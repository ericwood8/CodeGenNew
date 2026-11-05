using static CodeGenNew.Core.TemplateText;

namespace CodeGenNew.Core;

/// <summary> Feature flags in the Uno and WinUI3 apps (<c>FeatureTables</c> with the sign-in). The Uno app reads <c>GET /api/features</c> like the web front ends. The WinUI3 app reads the database itself, so it has the
/// rule of the API written for its own context: <c>FeatureFlags.AllAsync</c> reads every flag as the signed-in person sees it and <c>DesktopFeatures</c> keeps them while the person works. Both hide the screens
/// <c>FeatureScreens</c> names and have a page that switches a flag for the tenant. </summary>
public static class FeatureScreenCode
{
    /// <summary> Whether the WinUI3 app reads the flags: it signs in and the flag tables are valid. </summary>
    public static bool DesktopReads(DatabaseModel database, ProjectSettings project) =>
        TenantPlan.HasDesktopSignIn(project) && FeatureScreens.Reads(project) && FeatureFlagTables.Resolve(database, project).Plan is not null;

    /// <summary> Whether the WinUI3 app has the page that switches a flag for the tenant: the override table has a tenant column and the app works in a tenant. </summary>
    public static bool DesktopHasPage(DatabaseModel database, ProjectSettings project) =>
        DesktopReads(database, project) && TenantPlan.HasDesktopTenants(project) && FeatureFlagTables.Resolve(database, project).Plan is { OverrideTenant: not null };

    /// <summary> The flags of the signed-in person in the Uno app, read from the API after sign-in and again when the session changes. </summary>
    public static string UnoFlags(string servicesNamespace) => FillAll(UnoFlagsText, ("§SERVICES", servicesNamespace), ("§PATH", FeatureScreenWords.Path));

    public static string UnoPage(string viewNamespace, string servicesNamespace) => FillAll(UnoPageText, ("§NS", viewNamespace), ("§SERVICES", servicesNamespace), ("§PATH", FeatureScreenWords.Path),
        ("§LOADFAILED", FeatureScreenWords.LoadFailed), ("§SAVEFAILED", FeatureScreenWords.SaveFailed), ("§ROWS", RowText));

    public static string Xaml(string viewNamespace) => FillAll(XamlText, ("§NS", viewNamespace), ("§TITLE", FeatureScreenWords.Title), ("§HINT", FeatureScreenWords.Hint), ("§NOFEATURES", FeatureScreenWords.NoFeatures),
        ("§FEATURECOLUMN", FeatureScreenWords.FeatureColumn), ("§ONCOLUMN", FeatureScreenWords.OnColumn), ("§RESETBUTTON", FeatureScreenWords.ResetButton));

    /// <summary> The text of <c>Repositories/FeatureFlags.cs</c> after its namespace line: the rule of the API for the app's own context, and the flags the app keeps while the person works. </summary>
    public static string DesktopFile(DatabaseModel database, ProjectSettings project, string contextType, string[] usings)
    {
        var plan = FeatureFlagTables.Resolve(database, project).Plan!;
        (string, string)[] names = FeatureFlagWriter.Names(plan, contextType);
        string planRead = plan.PlanLink is { } link && DesktopSubscriptionCode.Applies(database, project)
            ? FillAll(PlanRead, names.Concat([("§PF", link.Table.TableName), ("§PFFLAG", link.Flag.CSharpName()), ("§PFPLAN", link.Plan.CSharpName())]).ToArray())
            : "        var planFlags = new HashSet<int>();\n";
        string tenantRow = plan.OverrideTenant is { } tenantColumn
            ? $"r.{plan.OverrideFlag.CSharpName()} == flag.Id && r.{tenantColumn.CSharpName()} == tenant{(plan.OverridePerson is { } p ? $" && r.{p.CSharpName()} == null" : "")}"
            : "false";
        string newTenant = plan.OverrideTenant is { } t ? $", {t.CSharpName()} = tenant" : "";
        var (tenantRows, personRows) = FeatureFlagWriter.Rows(plan);
        var o = new System.Text.StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using Microsoft.EntityFrameworkCore;\n\n");
        o.Append(FillAll(DesktopText, names.Concat([("§TENANTROWS", tenantRows), ("§PERSONROWS", personRows), ("§PLANREAD", planRead), ("§TENANTROW", tenantRow), ("§NEWTENANT", newTenant),
            ("§TENANTOF", TenantPlan.HasDesktopTenants(project) ? "user.TenantId" : "null")]).ToArray()));
        return o.ToString();
    }

    public static string DesktopPage(string viewNamespace, string repositoryNamespace, string contextType, string[] usings) => FillAll(DesktopPageText, ("§NS", viewNamespace), ("§REPO", repositoryNamespace), ("§CTX", contextType),
        ("§USINGS", string.Concat(usings.Select(u => $"using {u};\n"))), ("§NOTENANT", FeatureScreenWords.NoTenant), ("§SAVEFAILED", FeatureScreenWords.SaveFailed), ("§ROWS", RowText));

    /// <summary> The flag that hides a field of a WinUI3 edit dialog: the app reads the flags itself only when it signs in (WinUI3_Features). </summary>
    public static string? DesktopFieldFlag(FormModel form, ProjectSettings project, ColumnModel column) =>
        TenantPlan.HasDesktopSignIn(project) ? form.FeatureOf(column) : null;

    /// <summary> Whether a WinUI3 edit dialog has a field a flag hides. </summary>
    public static bool DesktopHasFieldFlags(FormModel form, ProjectSettings project) =>
        TenantPlan.HasDesktopSignIn(project) && form.FeatureFlags.Count > 0;

    /// <summary> The attribute that shows a field of an edit dialog only while its flag is on (<c>FeatureFields</c>); the view model has <c>FeatureVisibility</c>. </summary>
    public static string VisibilityAttribute(string flag) => $"Visibility=\"{{x:Bind ViewModel.FeatureVisibility('{flag.Replace("&", "&amp;").Replace("\"", "&quot;")}')}}\"";

    /// <summary> Wraps the save lines of a field a flag hides, so a field that is not shown keeps the value the row has. </summary>
    public static string SaveLines(string lines, string flag) =>
        $"        if (FeatureOn({CSharpText.Quote(flag)}))\n        {{\n" + string.Concat(lines.TrimEnd('\n').Split('\n').Select(l => "    " + l + "\n")) + "        }\n";

    /// <summary> The members the Uno view model of an edit dialog gains when a flag hides one of its fields. </summary>
    public static string UnoViewModelMembers(bool withAdminOnly = false) => ViewModelMembersText.Replace("§SOURCE", "_api.Features.Enabled(flag)") + (withAdminOnly ? AdminOnlyMembersText : "");

    /// <summary> The same for the WinUI3 view model, which reads the flags the window keeps. </summary>
    public static string DesktopViewModelMembers(bool withAdminOnly = false) => ViewModelMembersText.Replace("§SOURCE", "DesktopFeatures.Enabled(flag)") + (withAdminOnly ? AdminOnlyMembersText : "");

    /// <summary> The attribute for the administrator flag of the user table when a flag also hides it: shown to an administrator while the flag is on. </summary>
    public static string AdminOnlyVisibilityAttribute(string flag) => $"Visibility=\"{{x:Bind ViewModel.AdminOnlyFeatureVisibility('{flag.Replace("&", "&amp;").Replace("\"", "&quot;")}')}}\"";

    /// <summary> The save lines of the administrator flag when a flag also hides it. </summary>
    public static string AdminOnlySaveLines(string lines, string flag) =>
        $"        if (SignedInIsAdministrator() && FeatureOn({CSharpText.Quote(flag)}))\n        {{\n" + string.Concat(lines.TrimEnd('\n').Split('\n').Select(l => "    " + l + "\n")) + "        }\n";

    private const string AdminOnlyMembersText = """

    public Microsoft.UI.Xaml.Visibility AdminOnlyFeatureVisibility(string flag) => SignedInIsAdministrator() && FeatureOn(flag) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

""";

    private const string ViewModelMembersText = """

    private bool FeatureOn(string flag) => §SOURCE;

    public Microsoft.UI.Xaml.Visibility FeatureVisibility(string flag) => FeatureOn(flag) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

""";

    private const string RowText = """
/// <summary> A flag as the page lists it. </summary>
public sealed class FeatureRow
{
    public string Name { get; set; } = "";
    public bool On { get; set; }
}
""";

    private const string PlanRead = """
        var planFlags = new HashSet<int>();
        if (tenant is { } working && (await SubscriptionGuard.ReadAsync(context, working, includeLimits: false)).PlanId is { } planId)
            planFlags = (await context.Set<§PF>().IgnoreQueryFilters().AsNoTracking().Where(r => r.§PFPLAN == planId).Select(r => r.§PFFLAG).ToListAsync(cancellationToken)).ToHashSet();

""";

    private const string DesktopText = """
/// <summary> The feature flags as a tenant and a person see them (the rule of the API, read from the database the app uses): a flag is on or off by default, a flag the tenant's plan lists is on, the tenant's override replaces that and the person's override replaces both. </summary>
public static class FeatureFlags
{
    /// <summary> Every flag by name with its value for the tenant and the person (either may be null). </summary>
    public static async Task<Dictionary<string, bool>> AllAsync(§CTX context, int? tenant, int? person, CancellationToken cancellationToken = default)
    {
        var flags = await context.Set<§F>().IgnoreQueryFilters().AsNoTracking().Select(f => new { Id = f.§FKEY, Name = f.§FNAME, On = f.§FDEFAULT }).ToListAsync(cancellationToken);
        var tenantRows = await context.Set<§O>().IgnoreQueryFilters().AsNoTracking().Where(o => §TENANTROWS).Select(o => new { Flag = o.§OFLAG, On = o.§OON }).ToListAsync(cancellationToken);
        var personRows = await context.Set<§O>().IgnoreQueryFilters().AsNoTracking().Where(o => §PERSONROWS).Select(o => new { Flag = o.§OFLAG, On = o.§OON }).ToListAsync(cancellationToken);
§PLANREAD        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var flag in flags)
        {
            bool on = flag.On || planFlags.Contains(flag.Id);
            if (tenant is not null && tenantRows.FirstOrDefault(r => r.Flag == flag.Id) is { } byTenant)
                on = byTenant.On;
            if (person is not null && personRows.FirstOrDefault(r => r.Flag == flag.Id) is { } byPerson)
                on = byPerson.On;
            result[flag.Name] = on;
        }
        return result;
    }

    /// <summary> Switches a flag for the tenant (null removes the tenant's override) and answers every flag as the person sees it now; null when no flag has that name. </summary>
    public static async Task<Dictionary<string, bool>?> SetAsync(§CTX context, int tenant, int? person, string name, bool? enabled, CancellationToken cancellationToken = default)
    {
        var flag = await context.Set<§F>().IgnoreQueryFilters().AsNoTracking().Where(f => f.§FNAME == name).Select(f => new { Id = f.§FKEY }).FirstOrDefaultAsync(cancellationToken);
        if (flag is null)
            return null;
        var row = await context.Set<§O>().IgnoreQueryFilters().Where(r => §TENANTROW).FirstOrDefaultAsync(cancellationToken);
        if (enabled is not { } on)
        {
            if (row is not null)
                context.Remove(row);
        }
        else if (row is null)
            context.Add(new §O { §OFLAG = flag.Id, §OON = on§NEWTENANT });
        else
            row.§OON = on;
        await context.SaveChangesAsync(cancellationToken);
        return await AllAsync(context, tenant, person, cancellationToken);
    }
}

/// <summary> The flags of the signed-in person while they work: read at sign-in and at a switch of tenant, and again when the page changes one. </summary>
public static class DesktopFeatures
{
    private static Dictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary> Raised when the flags were replaced, so the window draws its menu again. </summary>
    public static event Action? Changed;

    public static IReadOnlyDictionary<string, bool> All => _flags;

    /// <summary> Whether the flag is on for the signed-in person; a name that is not a flag is off. </summary>
    public static bool Enabled(string name) => _flags.TryGetValue(name, out bool on) && on;

    public static void Set(Dictionary<string, bool> now)
    {
        _flags = new Dictionary<string, bool>(now, StringComparer.OrdinalIgnoreCase);
        Changed?.Invoke();
    }

    /// <summary> Reads the flags of the person (none while nobody is signed in). </summary>
    public static async Task LoadAsync(§CTX context, SignedInUser? user) =>
        _flags = user is null ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, bool>(await FeatureFlags.AllAsync(context, §TENANTOF, user.OwnerId), StringComparer.OrdinalIgnoreCase);
}

""";

    private const string XamlText = """
<Page
    x:Class="§NS.FeaturesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:§NS">

    <Grid Padding="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="§TITLE" Style="{ThemeResource TitleTextBlockStyle}" />
        <TextBlock Grid.Row="1" Text="§HINT" Style="{ThemeResource CaptionTextBlockStyle}" Opacity="0.7" />
        <InfoBar x:Name="Problem" Grid.Row="2" Severity="Error" IsOpen="False" IsClosable="False" />
        <TextBlock x:Name="EmptyText" Grid.Row="3" Text="§NOFEATURES" Visibility="Collapsed" />

        <ListView x:Name="FeatureList" Grid.Row="4" ItemsSource="{x:Bind Rows}" SelectionMode="None">
            <ListView.HeaderTemplate>
                <DataTemplate>
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="260" />
                            <ColumnDefinition Width="80" />
                            <ColumnDefinition Width="140" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="§FEATURECOLUMN" FontWeight="SemiBold" />
                        <TextBlock Grid.Column="1" Text="§ONCOLUMN" FontWeight="SemiBold" />
                    </Grid>
                </DataTemplate>
            </ListView.HeaderTemplate>
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="local:FeatureRow">
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="260" />
                            <ColumnDefinition Width="80" />
                            <ColumnDefinition Width="140" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{x:Bind Name}" VerticalAlignment="Center" />
                        <CheckBox Grid.Column="1" IsChecked="{x:Bind On}" Tag="{x:Bind Name}" AutomationProperties.Name="{x:Bind Name}" Click="OnToggleClick" />
                        <Button Grid.Column="2" Content="§RESETBUTTON" Tag="{x:Bind Name}" Click="OnResetClick" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
""";

    private const string UnoFlagsText = """
using System.Net.Http.Json;

namespace §SERVICES;

/// <summary> The feature flags of the signed-in person: every flag by name with its value for them (the default, their tenant's plan, their tenant's override and their own override applied). Read after sign-in and
/// again when the session changes; empty while signed out or when the API cannot say. </summary>
public sealed class FeatureFlags(HttpClient http, AuthSession session)
{
    private Dictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary> Raised when the flags were replaced, so the main page draws its menu again. </summary>
    public event Action? Changed;

    public IReadOnlyDictionary<string, bool> All => _flags;

    /// <summary> Whether the flag is on for the signed-in person; a name that is not a flag is off. </summary>
    public bool Enabled(string name) => _flags.TryGetValue(name, out bool on) && on;

    /// <summary> Replaces the flags (the page that switches a flag shows the API's answer). </summary>
    public void Set(Dictionary<string, bool> now)
    {
        _flags = new Dictionary<string, bool>(now, StringComparer.OrdinalIgnoreCase);
        Changed?.Invoke();
    }

    public async Task LoadAsync()
    {
        var now = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (session.IsSignedIn)
        {
            try
            {
                now = new Dictionary<string, bool>(await http.GetFromJsonAsync<Dictionary<string, bool>>("§PATH") ?? [], StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
            {
            }
        }
        _flags = now;
    }

    /// <summary> Reads the flags again while the person works. A read that fails keeps the flags the app has; answers whether any flag changed (the caller draws the menu again). </summary>
    public async Task<bool> RefreshAsync()
    {
        if (!session.IsSignedIn)
            return false;
        try
        {
            var now = new Dictionary<string, bool>(await http.GetFromJsonAsync<Dictionary<string, bool>>("§PATH") ?? [], StringComparer.OrdinalIgnoreCase);
            bool same = now.Count == _flags.Count && now.All(p => _flags.TryGetValue(p.Key, out bool on) && on == p.Value);
            _flags = now;
            return !same;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
""";

    private const string UnoPageText = """
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §SERVICES;

namespace §NS;

§ROWS

// A plain Page: the menu shows it to administrators. It lists every flag as the signed-in person sees it and switches one for their tenant (PUT §PATH/{name}).
public sealed partial class FeaturesPage : Page
{
    private readonly ApiClients _api;

    public ObservableCollection<FeatureRow> Rows { get; } = [];

    public FeaturesPage(ApiClients api)
    {
        _api = api;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private void Show(IEnumerable<KeyValuePair<string, bool>> flags)
    {
        Rows.Clear();
        foreach (var (name, on) in flags.OrderBy(f => f.Key, StringComparer.Ordinal))
            Rows.Add(new FeatureRow { Name = name, On = on });
        EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        try
        {
            Show(await _api.Http.GetFromJsonAsync<Dictionary<string, bool>>("§PATH") ?? []);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Problem.Message = "§LOADFAILED";
            Problem.IsOpen = true;
        }
    }

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        await ChangeAsync((string)box.Tag, box.IsChecked == true);
    }

    private async void OnResetClick(object sender, RoutedEventArgs e) => await ChangeAsync((string)((Button)sender).Tag, null);

    // A null removes the tenant's override; a refusal shows the API's sentence and the list shows what the API still has.
    private async Task ChangeAsync(string name, bool? enabled)
    {
        Problem.IsOpen = false;
        var response = await _api.Http.PutAsJsonAsync("§PATH/" + Uri.EscapeDataString(name), new { enabled });
        if (response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>() is { } flags)
        {
            _api.Features.Set(flags);
            Show(flags);
            return;
        }
        Problem.Message = await ApiSupport.ProblemDetailAsync(response) ?? "§SAVEFAILED";
        Problem.IsOpen = true;
        Show(_api.Features.All);
    }
}
""";

    private const string DesktopPageText = """
using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §REPO;
§USINGS
namespace §NS;

§ROWS

// A plain Page: the menu shows it to administrators. It lists every flag as the signed-in person sees it and switches one for the tenant the person works in.
public sealed partial class FeaturesPage : Page
{
    private readonly §CTX _context;

    public ObservableCollection<FeatureRow> Rows { get; } = [];

    public FeaturesPage(§CTX context)
    {
        _context = context;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private void Show(IEnumerable<KeyValuePair<string, bool>> flags)
    {
        Rows.Clear();
        foreach (var (name, on) in flags.OrderBy(f => f.Key, StringComparer.Ordinal))
            Rows.Add(new FeatureRow { Name = name, On = on });
        EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        if (_context.CurrentTenantId is null)
        {
            Problem.Message = "§NOTENANT";
            Problem.IsOpen = true;
            return;
        }
        await DesktopFeatures.LoadAsync(_context, DesktopSession.User);
        Show(DesktopFeatures.All);
    }

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        await ChangeAsync((string)box.Tag, box.IsChecked == true);
    }

    private async void OnResetClick(object sender, RoutedEventArgs e) => await ChangeAsync((string)((Button)sender).Tag, null);

    // A null removes the tenant's override; a change that cannot be saved shows its sentence and the list shows what the database still has.
    private async Task ChangeAsync(string name, bool? enabled)
    {
        Problem.IsOpen = false;
        if (_context.CurrentTenantId is not int tenant)
            return;
        try
        {
            if (await FeatureFlags.SetAsync(_context, tenant, DesktopSession.User?.OwnerId, name, enabled) is { } flags)
            {
                DesktopFeatures.Set(flags);
                Show(flags);
                return;
            }
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
        }
        Problem.Message = "§SAVEFAILED";
        Problem.IsOpen = true;
        Show(DesktopFeatures.All);
    }
}
""";
}
