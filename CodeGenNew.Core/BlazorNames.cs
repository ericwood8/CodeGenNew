using System.Data;

namespace CodeGenNew.Core;

/// <summary> What the Blazor templates agree on: which tables get a model and an API client, and the names they go by. The client templates, the page template and the template that registers the clients
/// must all use the same list, or a page asks for a client that was never written. </summary>
public static partial class BlazorNames
{
    /// <summary> The root namespace of the Blazor app: <c>&lt;ProjectName&gt;.Blazor</c>. </summary>
    public static string Namespace(ProjectSettings project) => (project.ProjectName ?? "MyApp") + ".Blazor";

    /// <summary> The model class and the client's stem: "SalesInvoice", "E_Customer" -> "Customer". </summary>
    public static string TypeName(string table) => ScreenNames.BaseName(table);

    /// <summary> The row of a table's lookup list (<c>GET /api/regions/lookup</c>) that the client class declares: <c>RegionLookup</c>. </summary>
    public static string LookupType(string table) => TypeName(table) + "Lookup";

    /// <summary> The type of a row of a parent's drop-down: the lookup row, or the model when the drop-down reads every row. </summary>
    public static string ItemType(FormParent parent) => parent.UsesLookupEndpoint ? LookupType(parent.Table) : TypeName(parent.Table);

    /// <summary> The property of a drop-down row that holds the parent's key. </summary>
    public static string KeyProperty(FormParent parent) => parent.UsesLookupEndpoint ? "Id" : parent.KeyName;

    /// <summary> The property of a drop-down row that holds the text to show. </summary>
    public static string DisplayProperty(FormParent parent) => parent.UsesLookupEndpoint ? "Name" : parent.DisplayName;

    /// <summary> The client call that fills a drop-down. </summary>
    public static string LoadCall(FormParent parent) => parent.UsesLookupEndpoint ? "GetLookupAsync" : "GetAllAsync";

    /// <summary> A name with everything that is not a letter or digit taken out, so it is a C# identifier part. </summary>
    public static string Identifier(string name) => NonAlphanumeric().Replace(name, "");

    /// <summary> The page field that holds the rows of a parent's drop-down: <c>lookupRegion</c>. </summary>
    public static string LookupField(FormParent parent) => "lookup" + Identifier(TypeName(parent.Table));

    /// <summary> The page field that holds the parent's rows a child grid names its foreign key from: <c>childLookupItem</c>. </summary>
    public static string ChildLookupField(string parentTable) => "childLookup" + Identifier(TypeName(parentTable));

    /// <summary> The page field that holds a child grid's rows: <c>childSalesInvoice</c>. </summary>
    public static string ChildVariable(ChildGrid child) => "child" + Identifier(TypeName(child.Table) + child.Role);

    /// <summary> The API route of a table, as the generated API serves it: <c>/api/customers</c>. </summary>
    public static string ApiRoute(string table) => "/api/" + ScreenNames.ApiRoute(table);

    /// <summary> The client over the table's OData entity set (the OData twin of the REST client): <c>CustomerODataClient</c>. </summary>
    public static string ODataClientName(string table) => TypeName(table) + "ODataClient";

    /// <summary> The route of a junction table's page ("ItemWarehouse" -> "item-warehouse-links") and its menu label ("Item Warehouse Links"). </summary>
    public static string JunctionRoute(string table) => JunctionPlan.Route(table);
    public static string JunctionLabel(string table) => JunctionPlan.Label(table);

    /// <summary> Whether the junction table gets a page: it is a many-to-many junction table and its anchor table has a lookup endpoint to pick a row from. </summary>
    public static bool HasJunctionPage(TableModel table, ProjectSettings project) => JunctionPlan.HasPage(table, project);

    /// <summary> The OData page's class, and the stem of its file: <c>CustomerODataPage</c>. </summary>
    public static string ODataPageName(string table) => TypeName(table) + "ODataPage";

    /// <summary> The key the browser keeps the signed-in session under: <c>acme.auth</c>, the same name the React app uses. </summary>
    public static string SessionKey(ProjectSettings project) => (project.ProjectName ?? "MyApp").ToLowerInvariant() + ".auth";

    /// <summary> Whether the table gets a model and a client: the API has CRUD for it (a name / active table's own API too) and its single key is an int, a guid or text (the routes take <c>{id:int}</c>, <c>{id:guid}</c> or <c>{id}</c>). </summary>
    public static bool HasClient(TableModel table, ProjectSettings project) => Refusal(table, project) is null;

    /// <summary> Whether the table also gets a client over its OData entity set: it has a client and is not a name / active table (the OData pages are the plain tables'). </summary>
    public static bool HasODataClient(TableModel table, ProjectSettings project) => HasClient(table, project) && !table.IsNameActiveTable;

    /// <summary> The reason a table gets no client, or null. <paramref name="stack"/> is the name the message gives the app (the Uno stack shares this rule). </summary>
    public static string? Refusal(TableModel table, ProjectSettings project, string stack = "Blazor")
    {
        if (!table.HasCrudApiOfAnyKey(project) && !table.HasNameActiveApi(project))
            return $"[{table.SchemaName}].[{table.TableName}] has no API of its own (an enum, a lookup table or a table with no entity), so there is nothing for a {stack} client to call.";
        var key = table.PrimaryKeyColumns.Count == 1 ? table.PrimaryKeyColumns[0] : null;
        if (key is null || KeyType.Of(key) is null)
            return $"the routes take the id as {{id:int}}, {{id:guid}} or {{id}} (text), but [{table.SchemaName}].[{table.TableName}] has "
                + (key is not null ? $"a {key.SqlTypeDeclaration} primary key." : table.PrimaryKeyColumns.Count == 0 ? "no primary key." : "a composite primary key.");
        return null;
    }

    [System.Text.RegularExpressions.GeneratedRegex("[^A-Za-z0-9]")]
    private static partial System.Text.RegularExpressions.Regex NonAlphanumeric();
}
