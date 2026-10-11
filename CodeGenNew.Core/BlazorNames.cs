using System.Data;

namespace CodeGenNew.Core;

/// <summary> What the Blazor templates agree on: which tables get a model and an API client, and the names they go by. The client templates, the page template and the template that registers the clients
/// must all use the same list, or a page asks for a client that was never written. </summary>
public static class BlazorNames
{
    /// <summary> The root namespace of the Blazor app: <c>&lt;ProjectName&gt;.Blazor</c>. </summary>
    public static string Namespace(ProjectSettings project) => (project.ProjectName ?? "MyApp") + ".Blazor";

    /// <summary> The model class and the client's stem: "SalesInvoice", "E_Customer" -> "Customer". </summary>
    public static string TypeName(string table) => ScreenNames.BaseName(table);

    /// <summary> A name with everything that is not a letter or digit taken out, so it is a C# identifier part. </summary>
    public static string Identifier(string name) => System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9]", "");

    /// <summary> The page field that holds the rows of a parent's drop-down: <c>lookupRegion</c>. </summary>
    public static string LookupField(FormParent parent) => "lookup" + Identifier(TypeName(parent.Table));

    /// <summary> The page field that holds the parent's rows a child grid names its foreign key from: <c>childLookupItem</c>. </summary>
    public static string ChildLookupField(string parentTable) => "childLookup" + Identifier(TypeName(parentTable));

    /// <summary> The page field that holds a child grid's rows: <c>childSalesInvoice</c>. </summary>
    public static string ChildVariable(ChildGrid child) => "child" + Identifier(TypeName(child.Table) + child.Role);

    /// <summary> The API route of a table, as the generated API serves it: <c>/api/customers</c>. </summary>
    public static string ApiRoute(string table) => "/api/" + ScreenNames.ApiRoute(table);

    /// <summary> Whether the table gets a model and a client: the API has CRUD for it, its single key is an int, a guid or text (the routes take <c>{id:int}</c>, <c>{id:guid}</c> or <c>{id}</c>) and it is not a name / active table. </summary>
    public static bool HasClient(TableModel table, ProjectSettings project) => Refusal(table, project) is null;

    /// <summary> The reason a table gets no client, or null. </summary>
    public static string? Refusal(TableModel table, ProjectSettings project)
    {
        if (!table.HasCrudApiOfAnyKey(project))
            return $"[{table.SchemaName}].[{table.TableName}] has no API of its own (an enum, a lookup table or a table with no entity), so there is nothing for a Blazor client to call.";
        var key = table.PrimaryKeyColumns.Count == 1 ? table.PrimaryKeyColumns[0] : null;
        if (key is null || KeyType.Of(key) is null)
            return $"the routes take the id as {{id:int}}, {{id:guid}} or {{id}} (text), but [{table.SchemaName}].[{table.TableName}] has "
                + (key is not null ? $"a {key.SqlTypeDeclaration} primary key." : table.PrimaryKeyColumns.Count == 0 ? "no primary key." : "a composite primary key.");
        if (table.IsNameActiveTable)
            return $"[{table.SchemaName}].[{table.TableName}] has a Name and an IsActive column, so its real API is hand-maintained and has no plain getAll(); write its client by hand.";
        return null;
    }
}
