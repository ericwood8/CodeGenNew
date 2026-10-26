using System.Text;

namespace CodeGenNew.Core;

/// <summary> What the dashboard's server and its three front ends agree on: the JSON the API answers (<c>DashboardResult</c> with its widgets, points and rows), the kinds a widget can be, which of them are
/// cards, and the style sheet and sparkline box every page uses. The server's classes, the TypeScript interfaces and the Blazor client classes are all written from here, so a change to the
/// contract is one edit. A widget with its data is called <c>DashboardWidgetData</c> everywhere; <see cref="DashboardWidget"/> is the plan's choice of a widget, which has no data yet. </summary>
public static class DashboardWire
{
    /// <summary> The width of the box a trend's line is drawn in. </summary>
    public const int SparkWidth = 200;

    /// <summary> The height of the box a trend's line is drawn in. </summary>
    public const int SparkHeight = 48;

    /// <summary> The kinds as the JSON names them, in the order of <see cref="DashboardWidgetKind"/>: <c>count</c>, <c>childCount</c>, ... </summary>
    public static IReadOnlyList<string> KindNames { get; } = Enum.GetValues<DashboardWidgetKind>().Select(KindName).ToList();

    /// <summary> The kind as the JSON names it. </summary>
    public static string KindName(DashboardWidgetKind kind) => JsonNames.Camel(kind.ToString());

    /// <summary> The kinds drawn as a card (a row of numbers); every other kind is a panel. </summary>
    public static IReadOnlyList<string> CardKinds { get; } = Enum.GetValues<DashboardWidgetKind>().Where(IsCard).Select(KindName).ToList();

    /// <summary> The kinds drawn as a list of bars. </summary>
    public static IReadOnlyList<string> BarKinds { get; } = [KindName(DashboardWidgetKind.TopBy), KindName(DashboardWidgetKind.Breakdown)];

    public static bool IsCard(DashboardWidgetKind kind) =>
        kind is DashboardWidgetKind.Count or DashboardWidgetKind.ChildCount or DashboardWidgetKind.Money or DashboardWidgetKind.Ratio or DashboardWidgetKind.Status;

    /// <summary> The card kinds as a C# pattern: <c>"count" or "childCount" or ...</c>. </summary>
    public static string CardKindsCSharp => string.Join(" or ", CardKinds.Select(k => $"\"{k}\""));

    /// <summary> The card kinds as a TypeScript array literal: <c>['count', 'childCount', ...]</c>. </summary>
    public static string CardKindsTypeScript => "[" + string.Join(", ", CardKinds.Select(k => $"'{k}'")) + "]";

    /// <summary> The method of the C# clients (Blazor and Uno) that reads the answer: every widget, or with a table the cards of that table's strip. </summary>
    public const string ClientGetCSharp =
        "public async Task<DashboardResult> GetAsync(string? table = null) =>\n" +
        "        await (await http.GetAsync(table is null ? \"/api/dashboard\" : \"/api/dashboard?table=\" + Uri.EscapeDataString(table))).ReadAsync<DashboardResult>();";

    /// <summary> The C# classes of the contract. The server writes them in the repository namespace and the Blazor client in its own; the widget is <c>partial</c> so the client can add what only a page needs. </summary>
    public static string CSharp()
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        Line("/// <summary> One value of a widget: a label and a number (a count, a sum, one month of a trend). </summary>");
        Line("public class DashboardPoint");
        Line("{");
        Line("    public string? Label { get; set; }");
        Line("    public double? Value { get; set; }");
        Line("}");
        Line();
        Line("/// <summary> One line of a recent-rows widget: what the row is called and its date. </summary>");
        Line("public class DashboardRow");
        Line("{");
        Line("    public string? Label { get; set; }");
        Line("    public string? Detail { get; set; }");
        Line("}");
        Line();
        Line($"/// <summary> A widget with its data. Kind is {KindList()}; a recent widget fills Rows, every other one Points. </summary>");
        Line("public partial class DashboardWidgetData");
        Line("{");
        Line("    public string Id { get; set; } = \"\";");
        Line("    public string Kind { get; set; } = \"\";");
        Line("    public string Title { get; set; } = \"\";");
        Line("    public string Table { get; set; } = \"\";");
        Line("    /// <summary> The route of the table's screen (empty when it has none), where a click on the widget goes. </summary>");
        Line("    public string Route { get; set; } = \"\";");
        Line("    /// <summary> True when the numbers are amounts of money, so a page shows them as currency. </summary>");
        Line("    public bool IsMoney { get; set; }");
        Line("    public List<DashboardPoint> Points { get; set; } = [];");
        Line("    public List<DashboardRow> Rows { get; set; } = [];");
        Line("}");
        Line();
        Line("public class DashboardResult");
        Line("{");
        Line("    public List<DashboardWidgetData> Widgets { get; set; } = [];");
        Line("}");
        return sb.ToString();
    }

    /// <summary> The TypeScript interfaces of the contract, indented by <paramref name="indent"/> spaces per level. </summary>
    public static string TypeScript(int indent)
    {
        string i = new(' ', indent);
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        Line("export interface DashboardPoint {");
        Line(i + "label: string | null;");
        Line(i + "value: number | null;");
        Line("}");
        Line();
        Line("export interface DashboardRow {");
        Line(i + "label: string | null;");
        Line(i + "detail: string | null;");
        Line("}");
        Line();
        Line("export type DashboardKind = " + string.Join(" | ", KindNames.Select(k => $"'{k}'")) + ";");
        Line();
        Line("export interface DashboardWidgetData {");
        Line(i + "id: string;");
        Line(i + "kind: DashboardKind;");
        Line(i + "title: string;");
        Line(i + "table: string;");
        Line(i + "route: string;");
        Line(i + "isMoney: boolean;");
        Line(i + "points: DashboardPoint[];");
        Line(i + "rows: DashboardRow[];");
        Line("}");
        Line();
        Line("export interface DashboardResult {");
        Line(i + "widgets: DashboardWidgetData[];");
        Line("}");
        return sb.ToString();
    }

    // "count, childCount, money, topBy, breakdown, trend, ratio, status or recent"
    private static string KindList() => string.Join(", ", KindNames.Take(KindNames.Count - 1)) + " or " + KindNames[^1];

    /// <summary> The kinds as a sentence fragment for a comment (<c>count, childCount, ... or recent</c>). </summary>
    public static string KindSentence => KindList();

    /// <summary> The style sheet every web dashboard uses: the cards, the bar list, the sparkline, the recent rows and the wide panel. Written beside the React page, inside the Angular widget and in the Blazor page.
    /// <paramref name="grid"/> adds the grid the widgets sit in (the Angular dashboard keeps it in its own sheet); <paramref name="wideSelector"/> is what makes a recent panel span two columns. </summary>
    public static string Css(string indent, bool grid, string wideSelector)
    {
        var rules = new List<string>();
        if (grid)
            rules.Add(".dash-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; margin: 12px 0; }");
        rules.AddRange(
        [
            ".dash-card, .dash-panel { padding: 12px 16px; border: 1px solid #8886; border-radius: 6px; background: #8881; }",
            ".dash-card h3, .dash-panel h3 { margin: 0 0 8px; font-size: 1rem; }",
            ".dash-card a, .dash-panel a { color: inherit; text-decoration: none; }",
            ".dash-card a:hover, .dash-panel a:hover { text-decoration: underline; }",
            ".dash-card dl { display: flex; flex-wrap: wrap; gap: 4px 20px; margin: 0; }",
            ".dash-card dt { font-size: 0.85rem; opacity: 0.8; }",
            ".dash-card dd { margin: 0 0 4px; font-size: 1.6rem; font-weight: 700; }",
            ".dash-bars { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }",
            ".dash-bars li { display: grid; grid-template-columns: minmax(80px, 30%) 1fr auto; align-items: center; gap: 8px; }",
            ".dash-track { display: block; height: 10px; border-radius: 5px; background: #8883; }",
            ".dash-fill { display: block; height: 100%; border-radius: 5px; background: currentColor; opacity: 0.55; }",
            ".dash-value { font-variant-numeric: tabular-nums; font-weight: 600; }",
            ".dash-spark { width: 100%; height: 56px; }",
            ".dash-spark polyline { fill: none; stroke: currentColor; stroke-width: 2; vector-effect: non-scaling-stroke; }",
            ".dash-recent { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }",
            ".dash-recent li { display: flex; justify-content: space-between; gap: 12px; }",
            ".dash-recent li span:last-child { white-space: nowrap; font-variant-numeric: tabular-nums; }",
            ".dash-panel details { margin-top: 8px; }",
            ".dash-panel summary { cursor: pointer; font-size: 0.85rem; opacity: 0.8; }",
            "@media (min-width: 720px) { " + wideSelector + " { grid-column: span 2; } }"
        ]);
        return string.Concat(rules.Select(r => indent + r + "\n"));
    }
}
