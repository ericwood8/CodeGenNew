using static CodeGenNew.Core.TemplateText;

namespace CodeGenNew.Core;

/// <summary> The subscription page of the Uno app (<c>SubscriptionTable</c> with the sign-in): the plan, the status, the period end and, for each plan limit, what the plan allows and what the tenant has, read from
/// <c>GET /api/subscription</c>. The page is in the menu for administrators. </summary>
public static class UnoSubscriptionCode
{
    /// <summary> The page; <paramref name="rowNamespace"/> is where <c>LimitRow</c> is (the view namespace in the Uno app, the repository namespace in the WinUI3 app, whose guard file holds it). </summary>
    public static string Xaml(string viewNamespace, string? rowNamespace = null) => FillAll(XamlText, ("§ROWNS", rowNamespace ?? viewNamespace), ("§NS", viewNamespace), ("§TITLE", SubscriptionScreenWords.Title), ("§LIMITS", SubscriptionScreenWords.LimitsHeading),
        ("§LIMITCOLUMN", SubscriptionScreenWords.LimitColumn), ("§ALLOWEDCOLUMN", SubscriptionScreenWords.AllowedColumn), ("§INUSECOLUMN", SubscriptionScreenWords.InUseColumn),
        ("§PLANSHEADING", SubscriptionScreenWords.PlansHeading), ("§PLANCOLUMN", SubscriptionScreenWords.PlanColumn), ("§PRICECOLUMN", SubscriptionScreenWords.PriceColumn), ("§ALLOWSCOLUMN", SubscriptionScreenWords.AllowsColumn), ("§CHOOSEBUTTON", SubscriptionScreenWords.ChooseButton));

    /// <summary> The row of the plans list and the classes it is read into (the Uno page reads them from the API; the WinUI3 guard file holds the same classes). </summary>
    public static string PlanRowClasses => FillAll(PlanRowText, ("§UNLIMITED", SubscriptionScreenWords.Unlimited), ("§CURRENT", SubscriptionScreenWords.CurrentMark));

    public static string Code(string viewNamespace, string servicesNamespace) => FillAll(CodeText, ("§NS", viewNamespace), ("§SERVICES", servicesNamespace), ("§PATH", SubscriptionScreenWords.Path),
        ("§PLANLABEL", SubscriptionScreenWords.PlanLabel), ("§STATUSLABEL", SubscriptionScreenWords.StatusLabel), ("§PERIODLABEL", SubscriptionScreenWords.PeriodEndLabel),
        ("§NOSUBSCRIPTION", SubscriptionScreenWords.NoSubscription), ("§LOADFAILED", SubscriptionScreenWords.LoadFailed), ("§PAUSED", SubscriptionScreenWords.PausedPrefix),
        ("§ACTIVE", SubscriptionScreenWords.Active), ("§UNLIMITED", SubscriptionScreenWords.Unlimited), ("§PRICELABEL", SubscriptionScreenWords.PriceLabel),
        ("§PLANROWS", PlanRowClasses), ("§CHOOSEFAILED", SubscriptionScreenWords.ChooseFailed));

    public const string PlanRowText = """
/// <summary> What a plan allows of one limit (null when it is unlimited). </summary>
public sealed class PlanLimitValue
{
    public string Key { get; set; } = "";
    public long? Allowed { get; set; }
}

/// <summary> A plan the tenant could choose, as a row of the plans list. </summary>
public sealed class PlanRow
{
    public int PlanId { get; set; }
    public string Name { get; set; } = "";
    public decimal? Price { get; set; }
    public string? Period { get; set; }
    public bool Current { get; set; }
    /// <summary> Whether the Choose button shows: the administrator may choose a plan and this is not the tenant's own. </summary>
    public bool CanChoose { get; set; }
    public List<PlanLimitValue> Limits { get; set; } = [];

    public string NameText => Current ? Name + " §CURRENT" : Name;
    public string PriceText => PriceTexts.Of(Price, Period);
    public string AllowsText => string.Join(", ", Limits.Select(l => l.Key + ": " + (l.Allowed is { } most ? most.ToString() : "§UNLIMITED")));
}

/// <summary> A price as the page shows it: two decimals and, when the plan has one, its billing period. </summary>
public static class PriceTexts
{
    public static string Of(decimal? price, string? period) =>
        price is { } amount ? amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(period) ? "" : " / " + period) : "";
}
""";

    private const string XamlText = """
<Page
    x:Class="§NS.SubscriptionPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:§NS"
    xmlns:rows="using:§ROWNS">

    <Grid Padding="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="§TITLE" Style="{ThemeResource TitleTextBlockStyle}" />

        <InfoBar x:Name="Problem" Grid.Row="1" Severity="Error" IsOpen="False" IsClosable="False" />

        <StackPanel Grid.Row="2" Spacing="4">
            <TextBlock x:Name="PlanText" />
            <TextBlock x:Name="PriceLine" />
            <TextBlock x:Name="StatusText" />
            <TextBlock x:Name="PeriodText" />
            <TextBlock x:Name="WritableText" FontWeight="SemiBold" />
        </StackPanel>

        <TextBlock x:Name="LimitsHeading" Grid.Row="3" Text="§LIMITS" Style="{ThemeResource SubtitleTextBlockStyle}" Visibility="Collapsed" />

        <ListView x:Name="LimitsList" Grid.Row="4" MaxHeight="220" ItemsSource="{x:Bind Limits}" SelectionMode="None">
            <ListView.HeaderTemplate>
                <DataTemplate>
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200" />
                            <ColumnDefinition Width="120" />
                            <ColumnDefinition Width="120" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="§LIMITCOLUMN" FontWeight="SemiBold" />
                        <TextBlock Grid.Column="1" Text="§ALLOWEDCOLUMN" FontWeight="SemiBold" />
                        <TextBlock Grid.Column="2" Text="§INUSECOLUMN" FontWeight="SemiBold" />
                    </Grid>
                </DataTemplate>
            </ListView.HeaderTemplate>
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="rows:LimitRow">
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200" />
                            <ColumnDefinition Width="120" />
                            <ColumnDefinition Width="120" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{x:Bind Key}" />
                        <TextBlock Grid.Column="1" Text="{x:Bind AllowedText}" />
                        <TextBlock Grid.Column="2" Text="{x:Bind Used}" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>

        <TextBlock x:Name="PlansHeading" Grid.Row="5" Text="§PLANSHEADING" Style="{ThemeResource SubtitleTextBlockStyle}" Visibility="Collapsed" />

        <ListView x:Name="PlansList" Grid.Row="6" ItemsSource="{x:Bind Plans}" SelectionMode="None">
            <ListView.HeaderTemplate>
                <DataTemplate>
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200" />
                            <ColumnDefinition Width="140" />
                            <ColumnDefinition Width="320" />
                            <ColumnDefinition Width="100" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="§PLANCOLUMN" FontWeight="SemiBold" />
                        <TextBlock Grid.Column="1" Text="§PRICECOLUMN" FontWeight="SemiBold" />
                        <TextBlock Grid.Column="2" Text="§ALLOWSCOLUMN" FontWeight="SemiBold" />
                    </Grid>
                </DataTemplate>
            </ListView.HeaderTemplate>
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="rows:PlanRow">
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200" />
                            <ColumnDefinition Width="140" />
                            <ColumnDefinition Width="320" />
                            <ColumnDefinition Width="100" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{x:Bind NameText}" />
                        <TextBlock Grid.Column="1" Text="{x:Bind PriceText}" />
                        <TextBlock Grid.Column="2" Text="{x:Bind AllowsText}" TextWrapping="Wrap" />
                        <Button Grid.Column="3" Content="§CHOOSEBUTTON" Tag="{x:Bind PlanId}" Click="OnChooseClick" Visibility="{x:Bind CanChoose}" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
""";

    private const string CodeText = """
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §SERVICES;

namespace §NS;

/// <summary> One plan limit: what the plan allows (null when it is unlimited) and what the tenant has. </summary>
public sealed class LimitRow
{
    public string Key { get; set; } = "";
    public long? Allowed { get; set; }
    public int Used { get; set; }

    public string AllowedText => Allowed is { } allowed ? allowed.ToString() : "§UNLIMITED";
}

/// <summary> The answer of GET /api/subscription. </summary>
public sealed class SubscriptionInfo
{
    public bool HasSubscription { get; set; }
    public string? PlanName { get; set; }
    public decimal? PlanPrice { get; set; }
    public string? PlanPeriod { get; set; }
    public string? Status { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public bool Writable { get; set; }
    public string? Reason { get; set; }
    public List<LimitRow> Limits { get; set; } = [];
    public List<PlanRow> Plans { get; set; } = [];
    public bool CanChoose { get; set; }
}

§PLANROWS
// A plain Page: the menu shows it to administrators. It reads the subscription of the tenant the person works in.
public sealed partial class SubscriptionPage : Page
{
    private readonly ApiClients _api;

    public ObservableCollection<LimitRow> Limits { get; } = [];
    public ObservableCollection<PlanRow> Plans { get; } = [];

    public SubscriptionPage(ApiClients api)
    {
        _api = api;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        Limits.Clear();
        Plans.Clear();
        try
        {
            var info = await _api.Http.GetFromJsonAsync<SubscriptionInfo>("§PATH");
            if (info is null || !info.HasSubscription)
            {
                PlanText.Text = "§NOSUBSCRIPTION";
                PriceLine.Text = "";
                StatusText.Text = "";
                PeriodText.Text = "";
                WritableText.Text = "";
            }
            else
            {
                PlanText.Text = "§PLANLABEL: " + info.PlanName;
                PriceLine.Text = info.PlanPrice is null ? "" : "§PRICELABEL: " + PriceTexts.Of(info.PlanPrice, info.PlanPeriod);
                StatusText.Text = "§STATUSLABEL: " + info.Status;
                PeriodText.Text = info.PeriodEnd is { } end ? "§PERIODLABEL: " + end.ToString("d") : "";
                WritableText.Text = info.Writable ? "§ACTIVE" : "§PAUSED" + info.Reason;
            }
            foreach (var limit in info?.Limits ?? [])
                Limits.Add(limit);
            LimitsHeading.Visibility = Limits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var plan in info?.Plans ?? [])
            {
                plan.CanChoose = info!.CanChoose && !plan.Current;
                Plans.Add(plan);
            }
            PlansHeading.Visibility = Plans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Problem.Message = "§LOADFAILED";
            Problem.IsOpen = true;
        }
    }

    // Choose moves the tenant to the plan of the row; a refusal shows the API's sentence.
    private async void OnChooseClick(object sender, RoutedEventArgs e)
    {
        var response = await _api.Http.PutAsJsonAsync("§PATH/plan", new { planId = (int)((Button)sender).Tag });
        if (response.IsSuccessStatusCode)
        {
            await LoadAsync();
            return;
        }
        Problem.Message = await ApiSupport.ProblemDetailAsync(response) ?? "§CHOOSEFAILED";
        Problem.IsOpen = true;
    }
}
""";
}
