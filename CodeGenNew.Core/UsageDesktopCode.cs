using static CodeGenNew.Core.TemplateText;

namespace CodeGenNew.Core;

/// <summary> The usage page of the desktop apps (<c>UsageTables</c> with the sign-in and the tenants): one XAML for Uno and WinUI3, a code-behind for Uno that asks the API and one for WinUI3 that reads the database through
/// the app's own context (the ledger, plus the monthly totals of <c>UsageRollup</c>, added up for the tenant the person works in). The list shows a row for each month and kind, with a filter on the kind. </summary>
public static class UsageDesktopCode
{
    public static string PageClass(UsagePage page) => page.PageClass;

    private static (string, string)[] Common(string viewNamespace, UsagePage page) =>
    [
        ("§NS", viewNamespace), ("§PAGE", page.PageClass), ("§ROW", page.Name + "Row"), ("§ROUTE", page.ApiRoute.TrimStart('/')), ("§TITLE", page.Label), ("§HINT", UsageScreenWords.Hint),
        ("§NOROWS", UsageScreenWords.NoRows), ("§LOADFAILED", UsageScreenWords.LoadFailed), ("§NOTENANT", UsageScreenWords.NoTenant), ("§MONTH", UsageScreenWords.MonthColumn), ("§KINDCOLUMN", UsageScreenWords.KindColumn),
        ("§QUANTITY", UsageScreenWords.QuantityColumn), ("§EVENTS", UsageScreenWords.EventsColumn), ("§FILTER", UsageScreenWords.KindFilter), ("§FROM", UsageScreenWords.FromFilter), ("§BEFORE", UsageScreenWords.BeforeFilter), ("§CHART", UsageScreenWords.ChartColumn), ("§OVERTIME", UsageScreenWords.OverTime), ("§MCOL", page.Name + "MonthColumn"), ("§BADDATE", UsageScreenWords.BadDate), ("§SHOW", UsageScreenWords.Show)
    ];

    public static string Xaml(string viewNamespace, UsagePage page) => FillAll(XamlText, Common(viewNamespace, page));

    public static string UnoPage(string viewNamespace, string servicesNamespace, UsagePage page) => FillAll(WithMonths(UnoText), [.. Common(viewNamespace, page), ("§SERVICES", servicesNamespace)]);

    private static string WithMonths(string text) => text.Replace("§MONTHCLASS", MonthClassText).Replace("§MONTHMETHOD", MonthMethodText);

    public static string DesktopPage(string viewNamespace, string repositoryNamespace, string contextType, string[] usings, UsagePage page)
    {
        var t = page.Shape;
        string monthly = "";
        var r = page.Rollup;
        if (r is not null)
            monthly = FillAll(MonthlyText, ("§M", r.Table.TableName), ("§MT", r.Tenant.CSharpName()), ("§MK", r.Kind.CSharpName()), ("§MM", r.Month.CSharpName()), ("§MQ", r.Quantity.CSharpName()));
        return FillAll(WithMonths(DesktopText), [.. Common(viewNamespace, page), ("§REPO", repositoryNamespace), ("§CTX", contextType), ("§USINGS", string.Concat(usings.Select(u => $"using {u};\n"))), ("§ENTITY", t.Table.TableName),
            ("§TENANTCOL", t.Tenant.CSharpName()), ("§KINDCOL", t.Kind.CSharpName()), ("§OCC", t.Occurred.CSharpName()), ("§QTYCOL", t.Quantity is null ? "1" : "x." + t.Quantity.CSharpName()), ("§MONTHLY", monthly)]);
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
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="§TITLE" Style="{ThemeResource TitleTextBlockStyle}" />
        <TextBlock Grid.Row="1" Text="§HINT" Style="{ThemeResource CaptionTextBlockStyle}" Opacity="0.7" TextWrapping="Wrap" />
        <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="8">
            <TextBox x:Name="KindBox" Header="§FILTER" Width="200" AutomationProperties.Name="§FILTER" />
            <TextBox x:Name="FromBox" Header="§FROM" Width="140" PlaceholderText="yyyy-mm-dd" AutomationProperties.Name="§FROM" />
            <TextBox x:Name="BeforeBox" Header="§BEFORE" Width="140" PlaceholderText="yyyy-mm-dd" AutomationProperties.Name="§BEFORE" />
            <Button Content="§SHOW" VerticalAlignment="Bottom" Click="OnShowClick" />
        </StackPanel>
        <InfoBar x:Name="Problem" Grid.Row="3" Severity="Error" IsOpen="False" IsClosable="False" />
        <TextBlock x:Name="EmptyText" Grid.Row="4" Text="§NOROWS" Visibility="Collapsed" />
        <StackPanel Grid.Row="5" Spacing="4">
            <TextBlock Text="§OVERTIME" FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{x:Bind Months}" AutomationProperties.Name="§OVERTIME">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="8" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="local:§MCOL">
                        <StackPanel VerticalAlignment="Bottom" HorizontalAlignment="Center">
                            <TextBlock Text="{x:Bind Total}" HorizontalAlignment="Center" />
                            <Rectangle Width="32" Height="{x:Bind Height}" Fill="{ThemeResource AccentFillColorDefaultBrush}" />
                            <TextBlock Text="{x:Bind Month}" HorizontalAlignment="Center" />
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
        <Grid Grid.Row="6" ColumnSpacing="12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="120" />
                <ColumnDefinition Width="80" />
                <ColumnDefinition Width="160" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="§MONTH" FontWeight="SemiBold" />
            <TextBlock Grid.Column="1" Text="§KINDCOLUMN" FontWeight="SemiBold" />
            <TextBlock Grid.Column="2" Text="§QUANTITY" FontWeight="SemiBold" />
            <TextBlock Grid.Column="3" Text="§EVENTS" FontWeight="SemiBold" />
            <TextBlock Grid.Column="4" Text="§CHART" FontWeight="SemiBold" />
        </Grid>
        <ListView Grid.Row="7" ItemsSource="{x:Bind Rows}" SelectionMode="None" BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}" BorderThickness="1" AutomationProperties.Name="§TITLE">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="local:§ROW">
                    <Grid ColumnSpacing="12" Padding="0,4">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="100" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="120" />
                            <ColumnDefinition Width="80" />
                            <ColumnDefinition Width="160" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{x:Bind Month}" />
                        <TextBlock Grid.Column="1" Text="{x:Bind Kind}" />
                        <TextBlock Grid.Column="2" Text="{x:Bind Quantity}" />
                        <TextBlock Grid.Column="3" Text="{x:Bind Events}" />
                        <ProgressBar Grid.Column="4" Minimum="0" Maximum="{x:Bind Max}" Value="{x:Bind Value}" AutomationProperties.Name="{x:Bind ChartName}" VerticalAlignment="Center" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
""";

    private const string UnoText = """
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §SERVICES;

namespace §NS;

public sealed class §ROW
{
    public string Month { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Events { get; set; } = "";
    public double Value { get; set; }
    public double Max { get; set; } = 1;
    public string ChartName => Month + " " + Kind;

    public override string ToString() => $"{Month} {Kind} {Quantity} {Events}";
}

§MONTHCLASS
// A plain Page: the menu shows it to administrators. It lists the months and kinds the API adds up for the tenant, with a filter on the kind.
public sealed partial class §PAGE : Page
{
    private readonly ApiClients _api;

    public ObservableCollection<§ROW> Rows { get; } = [];
    public ObservableCollection<§MCOL> Months { get; } = [];

    public §PAGE(ApiClients api)
    {
        _api = api;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private static string Text(Dictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";

    // A blank box is no limit; a day is written year-month-day.
    private bool TryDay(string text, out DateTime? day)
    {
        day = null;
        if (text.Trim().Length == 0)
            return true;
        if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        day = parsed;
        return true;
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        if (!TryDay(FromBox.Text, out var since) || !TryDay(BeforeBox.Text, out var before))
        {
            Problem.Message = "§BADDATE";
            Problem.IsOpen = true;
            return;
        }
        var query = new List<string>();
        if (KindBox.Text.Trim().Length > 0)
            query.Add("kind=" + Uri.EscapeDataString(KindBox.Text.Trim()));
        if (since is { } first)
            query.Add("from=" + first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (before is { } last)
            query.Add("to=" + last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        try
        {
            var lines = await _api.Http.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("§ROUTE" + (query.Count == 0 ? "" : "?" + string.Join("&", query))) ?? [];
            Rows.Clear();
            double most = Math.Max(1, lines.Select(l => double.TryParse(Text(l, "quantity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var units) ? units : 0).DefaultIfEmpty(0).Max());
            foreach (var line in lines)
                Rows.Add(new §ROW
                {
                    Month = Text(line, "month"), Kind = Text(line, "kind"), Quantity = Text(line, "quantity"), Events = Text(line, "events"), Max = most,
                    Value = double.TryParse(Text(line, "quantity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0
                });
            DrawMonths();
            EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Problem.Message = "§LOADFAILED";
            Problem.IsOpen = true;
        }
    }

    §MONTHMETHOD
    private async void OnShowClick(object sender, RoutedEventArgs e) => await LoadAsync();
}
""";

    private const string DesktopText = """
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §REPO;
§USINGS
namespace §NS;

public sealed class §ROW
{
    public string Month { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Events { get; set; } = "";
    public double Value { get; set; }
    public double Max { get; set; } = 1;
    public string ChartName => Month + " " + Kind;

    public override string ToString() => $"{Month} {Kind} {Quantity} {Events}";
}

§MONTHCLASS
// A plain Page: the menu shows it to administrators. It adds up the events of the tenant the person works in, month by month and kind by kind; the app reads the database itself.
public sealed partial class §PAGE : Page
{
    private readonly §CTX _context;

    public ObservableCollection<§ROW> Rows { get; } = [];
    public ObservableCollection<§MCOL> Months { get; } = [];

    public §PAGE(§CTX context)
    {
        _context = context;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    // A blank box is no limit; a day is written year-month-day.
    private bool TryDay(string text, out DateTime? day)
    {
        day = null;
        if (text.Trim().Length == 0)
            return true;
        if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        day = parsed;
        return true;
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        Rows.Clear();
        Months.Clear();
        if (_context.CurrentTenantId is not int tenant)
        {
            Problem.Message = "§NOTENANT";
            Problem.IsOpen = true;
            return;
        }
        string kind = KindBox.Text.Trim();
        if (!TryDay(FromBox.Text, out var since) || !TryDay(BeforeBox.Text, out var before))
        {
            Problem.Message = "§BADDATE";
            Problem.IsOpen = true;
            return;
        }
        var totals = new Dictionary<(int Year, int Month, string Kind), (decimal Units, int Events)>();
        var events = await _context.Set<§ENTITY>().AsNoTracking().Where(x => x.§TENANTCOL == tenant && (kind == "" || x.§KINDCOL == kind) && (since == null || x.§OCC >= since) && (before == null || x.§OCC < before))
            .Select(x => new { At = x.§OCC, Kind = x.§KINDCOL, Units = §QTYCOL }).ToListAsync();
        foreach (var e in events)
        {
            var key = (e.At.Year, e.At.Month, e.Kind);
            var have = totals.GetValueOrDefault(key);
            totals[key] = (have.Units + Convert.ToDecimal(e.Units), have.Events + 1);
        }
§MONTHLY        var ordered = totals.OrderByDescending(p => p.Key.Year).ThenByDescending(p => p.Key.Month).ThenBy(p => p.Key.Kind, StringComparer.Ordinal).ToList();
        double most = Math.Max(1, ordered.Select(p => (double)p.Value.Units).DefaultIfEmpty(0).Max());
        foreach (var ((year, month, name), (units, count)) in ordered)
            Rows.Add(new §ROW { Month = $"{year:0000}-{month:00}", Kind = name, Quantity = units.ToString("0.####", CultureInfo.InvariantCulture), Events = count.ToString(), Value = (double)units, Max = most });
        DrawMonths();
        EmptyText.Visibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    §MONTHMETHOD
    private async void OnShowClick(object sender, RoutedEventArgs e) => await LoadAsync();
}
""";

    private const string MonthClassText = """
public sealed class §MCOL
{
    public string Month { get; set; } = "";
    public string Total { get; set; } = "";
    public double Height { get; set; }

    public override string ToString() => $"{Month} {Total}";
}

""";

    // One column for each month, oldest first, as tall as the units of all the kinds listed.
    private const string MonthMethodText = """
private void DrawMonths()
    {
        Months.Clear();
        var sums = Rows.GroupBy(r => r.Month).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (Month: g.Key, Units: g.Sum(r => r.Value))).ToList();
        double peak = Math.Max(1, sums.Select(s => s.Units).DefaultIfEmpty(0).Max());
        foreach (var (month, units) in sums)
            Months.Add(new §MCOL { Month = month, Total = units.ToString("0.####", CultureInfo.InvariantCulture), Height = Math.Round(units / peak * 100) });
    }
""";

    private const string MonthlyText = """
        var rolled = await _context.Set<§M>().AsNoTracking().Where(r => r.§MT == tenant && (kind == "" || r.§MK == kind)).Select(r => new { Month = r.§MM, Kind = r.§MK, Units = r.§MQ }).ToListAsync();
        foreach (var r in rolled)
        {
            var start = new DateTime(r.Month.Year, r.Month.Month, 1);
            if ((since is { } first && start < first) || (before is { } last && start.AddMonths(1) > last))
                continue;
            var key = (r.Month.Year, r.Month.Month, r.Kind);
            var have = totals.GetValueOrDefault(key);
            totals[key] = (have.Units + Convert.ToDecimal(r.Units), have.Events);
        }

""";
}
