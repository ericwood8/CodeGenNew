namespace CodeGenNew.Core;

/// <summary> The dashboard drawing of the desktop apps (WinUI3 and Uno): one static class that builds a widget of the dashboard's answer from plain controls, so no chart package is needed. Both stacks write
/// the same text from here; they differ in the namespace of the page, the namespace of the classes <see cref="DashboardWire"/> writes, and the screens template that tags a screen with its table's name. </summary>
public static class DesktopDashboardCode
{
    /// <summary> The text of <c>DashboardWidgetViews.cs</c>. <paramref name="dataSource"/> is what the comment says the widgets come from (<c>DashboardData</c>, <c>the dashboard</c>). </summary>
    public static string WidgetViews(string viewNamespace, string dataNamespace, string screensTemplate, string dataSource) => Template
        .Replace("§VIEW§", viewNamespace).Replace("§DATA§", dataNamespace).Replace("§SCREENS§", screensTemplate).Replace("§SOURCE§", dataSource)
        .Replace("§CARDS§", DashboardWire.CardKindsCSharp)
        .Replace("§SW§", DashboardWire.SparkWidth.ToString())
        .Replace("§SH2§", (DashboardWire.SparkHeight - 2).ToString()).Replace("§SH4§", (DashboardWire.SparkHeight - 4).ToString()).Replace("§SH§", DashboardWire.SparkHeight.ToString()) + "\n";

    /// <summary> The text of <c>DashboardStrip.cs</c>: the cards of one table above its list page. WinUI3 reads them from <c>DashboardData</c> through a context of its own, Uno from the dashboard client;
    /// <paramref name="dataUsings"/> and <paramref name="extraUsings"/> are whole <c>using</c> lines, <paramref name="loadLines"/> the statements that set <c>result</c>. </summary>
    public static string StripFile(string viewNamespace, string dataUsings, string extraUsings, string clientField, string constructorArguments, string constructorBody, string loadLines) => StripTemplate
        .Replace("§VIEW§", viewNamespace).Replace("§USINGS§", dataUsings).Replace("§EXTRA§", extraUsings).Replace("§FIELD§", clientField)
        .Replace("§ARGS§", constructorArguments).Replace("§INIT§", constructorBody).Replace("§LOAD§", loadLines) + "\n";

    private const string StripTemplate = """
using System;
using System.Threading.Tasks;
§USINGS§using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
§EXTRA§
namespace §VIEW§;

// The two or three cards of one table, above its list page. A failure shows nothing: the list below is what the person came for.
public sealed class DashboardStrip : UserControl
{
§FIELD§    private readonly string _table;
    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(16, 8, 16, 0) };

    public DashboardStrip(§ARGS§)
    {
§INIT§        _table = table;
        Content = _cards;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
§LOAD§            _cards.Children.Clear();
            foreach (var widget in result.Widgets)
                _cards.Children.Add(DashboardWidgetViews.Build(widget, null));
        }
        catch (Exception)
        {
            _cards.Children.Clear();
        }
    }
}
""";

    private const string Template = """
using System;
using System.Collections.Generic;
using System.Linq;
using §DATA§;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace §VIEW§;

// Draws one widget of §SOURCE§ as plain controls. The numbers are always text: a bar or a line is an extra, never the only way to read a value.
internal static class DashboardWidgetViews
{
    private static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.CurrentCulture;

    public static bool IsCard(DashboardWidgetData widget) => widget.Kind is §CARDS§;

    private static string Format(double? value, bool money) => value is null ? "" : value.Value.ToString(money ? "C2" : "#,##0.##", Culture);

    private static Brush ThemeBrush(string key, Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var found) && found is Brush brush ? brush : new SolidColorBrush(fallback);

    public static UIElement Build(DashboardWidgetData widget, Action<string>? navigate)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(TitleOf(widget, navigate));
        switch (widget.Kind)
        {
            case "topBy":
            case "breakdown":
                AddBars(body, widget);
                break;
            case "trend":
                AddTrend(body, widget);
                break;
            case "recent":
                AddRecent(body, widget);
                break;
            default:
                AddCard(body, widget);
                break;
        }

        return new Border
        {
            Child = body,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = ThemeBrush("SolidBackgroundFillColorSecondaryBrush", Color.FromArgb(0x20, 0x80, 0x80, 0x80)),
            BorderBrush = ThemeBrush("ControlStrokeColorSecondaryBrush", Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            MinWidth = 260,
            MaxWidth = widget.Kind == "recent" ? 880 : 420
        };
    }

    private static UIElement TitleOf(DashboardWidgetData widget, Action<string>? navigate)
    {
        // §SCREENS§ tags a screen with the table's name, so the widget's table is where a click goes.
        if (navigate is null || string.IsNullOrEmpty(widget.Route))
            return new TextBlock { Text = widget.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };

        var link = new HyperlinkButton { Content = widget.Title, Padding = new Thickness(0), FontWeight = FontWeights.SemiBold };
        link.Click += (_, _) => navigate(widget.Table);
        return link;
    }

    private static void AddCard(StackPanel body, DashboardWidgetData widget)
    {
        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        foreach (var point in widget.Points)
        {
            var one = new StackPanel();
            one.Children.Add(new TextBlock { Text = point.Label, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
            one.Children.Add(new TextBlock { Text = Format(point.Value, widget.IsMoney), FontSize = 26, FontWeight = FontWeights.Bold });
            wrap.Children.Add(one);
        }

        body.Children.Add(wrap);
    }

    private static void AddBars(StackPanel body, DashboardWidgetData widget)
    {
        if (widget.Points.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = "No data." });
            return;
        }

        double largest = Math.Max(widget.Points.Max(p => p.Value ?? 0), 1);
        foreach (var point in widget.Points)
        {
            double share = Math.Clamp((point.Value ?? 0) / largest, 0, 1);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = point.Label, TextTrimming = TextTrimming.CharacterEllipsis });

            var track = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - share, 0.001), GridUnitType.Star) });
            var fill = new Border { CornerRadius = new CornerRadius(5), Background = ThemeBrush("AccentFillColorDefaultBrush", Colors.SteelBlue) };
            track.Children.Add(fill);
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var value = new TextBlock { Text = Format(point.Value, widget.IsMoney), FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            body.Children.Add(row);
        }
    }

    private static void AddTrend(StackPanel body, DashboardWidgetData widget)
    {
        if (widget.Points.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = "No data." });
            return;
        }

        var values = widget.Points.Select(p => p.Value ?? 0).ToList();
        double max = Math.Max(values.Max(), 0), min = Math.Min(values.Min(), 0), span = max - min == 0 ? 1 : max - min;
        double step = values.Count > 1 ? §SW§.0 / (values.Count - 1) : 0;
        var line = new Polyline { Stroke = ThemeBrush("TextFillColorPrimaryBrush", Colors.Gray), StrokeThickness = 2 };
        for (int i = 0; i < values.Count; i++)
            line.Points.Add(new Windows.Foundation.Point(i * step, §SH2§ - ((values[i] - min) / span) * §SH4§));
        body.Children.Add(new Canvas { Width = §SW§, Height = §SH§, Children = { line } });

        var numbers = new Expander { Header = "Show the numbers", HorizontalAlignment = HorizontalAlignment.Stretch };
        var list = new StackPanel();
        foreach (var point in widget.Points)
            list.Children.Add(new TextBlock { Text = $"{point.Label}   {Format(point.Value, widget.IsMoney)}" });
        numbers.Content = list;
        body.Children.Add(numbers);
    }

    private static void AddRecent(StackPanel body, DashboardWidgetData widget)
    {
        if (widget.Rows.Count == 0)
            body.Children.Add(new TextBlock { Text = "No rows." });
        foreach (var row in widget.Rows)
        {
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(new TextBlock { Text = row.Label, TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = new TextBlock { Text = row.Detail, TextWrapping = TextWrapping.NoWrap };
            Grid.SetColumn(detail, 1);
            line.Children.Add(detail);
            body.Children.Add(line);
        }
    }
}
""";
}
