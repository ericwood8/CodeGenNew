namespace CodeGenNew.Core;

/// <summary> The names a table's screen goes by, in one place: the menu text, the web route and the file / class stems. The master-detail screens link
/// a child row to its screen by route, and the generated menu lists the screens by the same route, so both must come from here. </summary>
public static class ScreenNames
{
    /// <summary> The table's name without a leading E_ or SY_ prefix (the templates' own rule). </summary>
    public static string BaseName(string table) => TemplateHelpers.BaseName(table);

    /// <summary> "SalesInvoice" -> "Sales Invoice". </summary>
    public static string Words(string name) => Labels.Words(name);

    /// <summary> The menu text of the table's screen. </summary>
    public static string Label(string table) => Words(BaseName(table));

    /// <summary> The web route of the table's screen, without the slash: "SalesInvoice" -> "sales-invoice". </summary>
    public static string Route(string table) => Label(table).ToLowerInvariant().Replace(' ', '-');

    /// <summary> The lower-case stem Angular files are named from: "SalesInvoice" -> "salesinvoice". </summary>
    public static string Stem(string table) => BaseName(table).ToLowerInvariant();

    /// <summary> The route segment of the table's API, without the slash and the <c>api</c> prefix: "SalesInvoice" -> "salesinvoices". The generated clients and the API's own routes both use this. </summary>
    public static string ApiRoute(string table) => Stem(table).Pluralize();
}
