using static CodeGenNew.Core.TemplateText;

namespace CodeGenNew.Core;

/// <summary> The sessions and tokens page of the desktop apps (<c>TokenTables</c>): one XAML for Uno and WinUI3, a code-behind for Uno that asks the API and one for WinUI3 that asks the database through the app's own context. The list shows
/// each row as a line of text with its active state, a Revoke button, and a Sign out everywhere button when the table points at a person. </summary>
public static class TokenDesktopCode
{
    /// <summary> The pages of the WinUI3 app: it signs in against the user table itself. </summary>
    public static IReadOnlyList<TokenPage> DesktopPages(DatabaseModel database, ProjectSettings project) =>
        TenantPlan.HasDesktopSignIn(project) ? TokenScreens.Pages(database, project) : [];

    public static string PageClass(TokenPage page) => page.Name + "Page";

    public static string Xaml(string viewNamespace, TokenPage page) => WithoutSignOut(page, FillAll(XamlText, ("§NS", viewNamespace), ("§PAGE", PageClass(page)), ("§ROW", page.Name + "Row"), ("§TITLE", page.Label), ("§HINT", TokenScreenWords.Hint),
        ("§NOROWS", TokenScreenWords.NoRows), ("§REVOKE", TokenScreenWords.Revoke), ("§SIGNOUT", TokenScreenWords.SignOut)));

    /// <summary> A table with no person has no Sign out everywhere button, so its page has no handler for it. </summary>
    private static string WithoutSignOut(TokenPage page, string xaml) =>
        page.PersonJson is not null ? xaml : string.Join("\n", xaml.Split('\n').Where(line => !line.Contains("OnSignOutClick")));

    public static string UnoPage(string viewNamespace, string servicesNamespace, TokenPage page) => FillAll(UnoText, Common(viewNamespace, page).Concat([("§SERVICES", servicesNamespace)]).ToArray());

    public static string DesktopPage(string viewNamespace, string repositoryNamespace, string contextType, string[] usings, TokenPage page)
    {
        var t = page.Shape;
        string signOut = t.Person is null ? "" : $$"""

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        Problem.IsOpen = false;
        var person = {{t.Person.CSharpBase()}}.Parse((string)((Button)sender).Tag);
        var now = DateTime.Now;
        int ended = await _context.Set<{{t.Table.TableName}}>().Where(x => x.{{t.Person.CSharpName()}} == person && {{TokenWriter.ActiveTest(t)}}).ExecuteUpdateAsync(s => s{{TokenWriter.EndClause(t)}});
        Note.Message = ended + "{{TokenScreenWords.EndedSuffix}}";
        Note.IsOpen = true;
        await LoadAsync();
    }

""";
        string select = string.Join(", ", t.Shown.Select(c => $"x.{c.CSharpName()}")) + $", Active = {TokenWriter.ActiveTest(t)}";
        string summary = string.Join(" + \"   \" + ", page.Columns.Select((c, i) => $"\"{c.Label}: \" + Fmt(row.{CSharpNameOf(t, c)})"));
        return FillAll(DesktopText, Common(viewNamespace, page).Concat(
        [
            ("§REPO", repositoryNamespace), ("§CTX", contextType), ("§USINGS", string.Concat(usings.Select(u => $"using {u};\n"))), ("§ENTITY", t.Table.TableName), ("§KEY", t.Key.CSharpName()), ("§SELECT", select),
            ("§SUMMARY", summary.Length == 0 ? "\"\"" : summary), ("§PERSONARG", t.Person is null ? "\"\"" : $"Fmt(row.{t.Person.CSharpName()})"), ("§ACTIVE", TokenWriter.ActiveTest(t)), ("§END", TokenWriter.EndClause(t)),
            ("§KEYTYPE", t.Key.CSharpBase()), ("§SIGNOUT", signOut)
        ]).ToArray());
    }

    private static string CSharpNameOf(TokenShape t, TokenColumn c) => t.Shown.First(s => JsonNames.Camel(s.Name) == c.Json).CSharpName();

    private static (string, string)[] Common(string viewNamespace, TokenPage page)
    {
        string columns = string.Join(", ", page.Columns.Select(c => $"(\"{c.Json}\", \"{c.Label}\", \"{c.Kind}\")"));
        return
        [
            ("§NS", viewNamespace), ("§PAGE", PageClass(page)), ("§ROW", page.Name + "Row"), ("§ROUTE", page.ApiRoute.TrimStart('/')), ("§COLUMNS", columns), ("§KEYJSON", page.KeyJson),
            ("§PERSONJSON", page.PersonJson is null ? "null" : $"\"{page.PersonJson}\""), ("§LOADFAILED", TokenScreenWords.LoadFailed), ("§CHANGEFAILED", TokenScreenWords.ChangeFailed),
            ("§YES", TokenScreenWords.Yes), ("§REVOKE", TokenScreenWords.Revoke), ("§NO", TokenScreenWords.No), ("§ENDED", TokenScreenWords.EndedSuffix)
        ];
    }

    private const string XamlText = """
<Page
    x:Class="§NS.§PAGE"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:§NS">

    <Grid Padding="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="§TITLE" Style="{ThemeResource TitleTextBlockStyle}" />
        <TextBlock Grid.Row="1" Text="§HINT" Style="{ThemeResource CaptionTextBlockStyle}" Opacity="0.7" TextWrapping="Wrap" />
        <InfoBar x:Name="Problem" Grid.Row="2" Severity="Error" IsOpen="False" IsClosable="False" />
        <InfoBar x:Name="Note" Grid.Row="3" Severity="Success" IsOpen="False" />
        <TextBlock x:Name="EmptyText" Grid.Row="4" Text="§NOROWS" Visibility="Collapsed" />
        <ListView Grid.Row="5" ItemsSource="{x:Bind Rows}" SelectionMode="None" BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}" BorderThickness="1" AutomationProperties.Name="§TITLE">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="local:§ROW">
                    <Grid ColumnSpacing="12" BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}" BorderThickness="0,0,0,1" Padding="0,6">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="60" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{x:Bind Summary}" TextWrapping="Wrap" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="1" Text="{x:Bind ActiveText}" VerticalAlignment="Center" />
                        <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="8">
                            <Button Content="§REVOKE" Tag="{x:Bind Key}" IsEnabled="{x:Bind Active}" Click="OnRevokeClick" AutomationProperties.Name="{x:Bind RevokeName}" />
                            <Button Content="§SIGNOUT" Tag="{x:Bind Person}" Visibility="{x:Bind SignOutVisibility}" Click="OnSignOutClick" />
                        </StackPanel>
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
""";

    private const string UnoText = """
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §SERVICES;

namespace §NS;

public sealed class §ROW
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool Active { get; set; }
    public string Person { get; set; } = "";
    public string ActiveText => Active ? "§YES" : "§NO";
    public string RevokeName => "§REVOKE " + Key;
    public Visibility SignOutVisibility => Person.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

// A plain Page: the menu shows it to administrators. It lists the rows the API answers without the hash and revokes one, or every active row of a person.
public sealed partial class §PAGE : Page
{
    private const string KeyColumn = "§KEYJSON";
    private static readonly string? PersonColumn = §PERSONJSON;
    private static readonly (string Json, string Label, string Kind)[] Columns = [§COLUMNS];

    private readonly ApiClients _api;

    public ObservableCollection<§ROW> Rows { get; } = [];

    public §PAGE(ApiClients api)
    {
        _api = api;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private static string Text(Dictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";

    private static string Show(string kind, Dictionary<string, JsonElement> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";
        if (kind == "flag")
            return value.ValueKind == JsonValueKind.True ? "§YES" : "§NO";
        return kind == "date" && DateTime.TryParse(value.ToString(), out var when) ? when.ToString("g") : value.ToString();
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        try
        {
            var rows = await _api.Http.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("§ROUTE") ?? [];
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(new §ROW
                {
                    Key = Text(row, KeyColumn),
                    Summary = string.Join("   ", Columns.Select(c => c.Label + ": " + Show(c.Kind, row, c.Json))),
                    Active = row.TryGetValue("active", out var flag) && flag.ValueKind == JsonValueKind.True,
                    Person = PersonColumn is null ? "" : Text(row, PersonColumn)
                });
            EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Problem.Message = "§LOADFAILED";
            Problem.IsOpen = true;
        }
    }

    private async void OnRevokeClick(object sender, RoutedEventArgs e) => await PostAsync("§ROUTE/" + Uri.EscapeDataString((string)((Button)sender).Tag) + "/revoke", false);

    private async void OnSignOutClick(object sender, RoutedEventArgs e) => await PostAsync("§ROUTE/revoke-person/" + Uri.EscapeDataString((string)((Button)sender).Tag), true);

    private async Task PostAsync(string path, bool count)
    {
        Problem.IsOpen = false;
        Note.IsOpen = false;
        var response = await _api.Http.PostAsync(path, null);
        if (!response.IsSuccessStatusCode)
        {
            Problem.Message = "§CHANGEFAILED";
            Problem.IsOpen = true;
        }
        else if (count && await response.Content.ReadFromJsonAsync<Dictionary<string, int>>() is { } answer)
        {
            Note.Message = answer.GetValueOrDefault("revoked") + "§ENDED";
            Note.IsOpen = true;
        }
        await LoadAsync();
    }
}
""";

    private const string DesktopText = """
using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §REPO;
§USINGS
namespace §NS;

public sealed class §ROW
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool Active { get; set; }
    public string Person { get; set; } = "";
    public string ActiveText => Active ? "§YES" : "§NO";
    public string RevokeName => "§REVOKE " + Key;
    public Visibility SignOutVisibility => Person.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

// A plain Page: the menu shows it to administrators. It lists the rows without the hash and revokes one, or every active row of a person; the app reads the database itself.
public sealed partial class §PAGE : Page
{
    private readonly §CTX _context;

    public ObservableCollection<§ROW> Rows { get; } = [];

    public §PAGE(§CTX context)
    {
        _context = context;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private static string Fmt(object? value) => value switch { null => "", bool flag => flag ? "§YES" : "§NO", DateTime moment => moment.ToString("g"), _ => value.ToString() ?? "" };

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        var now = DateTime.Now;
        var rows = await _context.Set<§ENTITY>().AsNoTracking().OrderByDescending(x => x.§KEY).Take(200).Select(x => new { §SELECT }).ToListAsync();
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(new §ROW { Key = Fmt(row.§KEY), Summary = §SUMMARY, Active = row.Active, Person = §PERSONARG });
        EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRevokeClick(object sender, RoutedEventArgs e)
    {
        Problem.IsOpen = false;
        Note.IsOpen = false;
        var id = §KEYTYPE.Parse((string)((Button)sender).Tag);
        var now = DateTime.Now;
        await _context.Set<§ENTITY>().Where(x => x.§KEY == id && §ACTIVE).ExecuteUpdateAsync(s => s§END);
        await LoadAsync();
    }
§SIGNOUT}
""";
}
