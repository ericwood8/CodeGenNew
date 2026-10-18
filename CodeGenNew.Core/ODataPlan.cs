namespace CodeGenNew.Core;

/// <summary> A table the project asked to serve as OData that the generator will not, and why. </summary>
public sealed record ODataRefusal(string Table, string Reason);

/// <summary> The tables served as OData and the refusals to write into the generated file as comments. </summary>
public sealed record ODataSelection(IReadOnlyList<TableModel> Tables, IReadOnlyList<ODataRefusal> Refusals);

/// <summary> Names of the OData endpoints; the set name is the REST route's name so <c>/api/customers</c> and <c>/odata/customers</c> pair up. </summary>
public static class ODataNames
{
    public const string Prefix = "/odata";

    public static string SetName(string table) => ScreenNames.ApiRoute(table);

    public static string Route(string table) => Prefix + "/" + SetName(table);
}

/// <summary> Which tables the API serves as OData (<c>OData=true</c>, <c>ODataTables</c>). The OData routes read the context directly, so a table whose REST routes enforce a rule the OData routes do not
/// yet apply is refused instead of left open. Every template that writes OData for a table asks this one rule. </summary>
public static class ODataPlan
{
    public const string NoCrudApi = "has no CRUD API of its own (it needs a single whole-number, uniqueidentifier or text key, and is not an enum)";

    private static bool NamesAll(ProjectSettings project) => project.ODataTables.Length == 0 || project.ODataTables.Any(r => r == "*");

    private static bool IsRequested(TableModel table, ProjectSettings project) =>
        NamesAll(project) || project.ODataTables.Any(r => r.EqualsIgnoreCase(table.TableName));

    /// <summary> The table is served as OData under these settings: <c>OData=true</c>, it has a CRUD API, the list names it (or is blank) and nothing refuses it. A per-table template asks this without the database. </summary>
    /// <summary> The table has an API to serve: a plain CRUD API of any key, or the API of a name/active table (a Name and an IsActive column, whose OData routes keep its duplicate-name rule). </summary>
    public static bool HasApi(TableModel table, ProjectSettings project) => table.HasCrudApiOfAnyKey(project) || table.HasNameActiveApi(project);

    public static bool Serves(TableModel table, ProjectSettings project) =>
        project.OData && HasApi(table, project) && IsRequested(table, project) && RefusalOf(table, project) is null;

    /// <summary> The tables that get an Angular OData screen: served and listed as a screen of the Angular stack (a master-detail table gets the OData form of the master-detail screen; a name/active table has none yet). </summary>
    public static List<TableModel> AngularScreenTables(DatabaseModel database, ProjectSettings project) =>
        database.ScreenTables(project, "Angular").Where(t => Serves(t, project)).ToList();

    /// <summary> The tables that get a React OData page: served and listed as a screen of the React stack (a name/active table has the page of its own kind). </summary>
    public static List<TableModel> ReactScreenTables(DatabaseModel database, ProjectSettings project) =>
        database.ScreenTables(project, "React").Where(t => Serves(t, project)).ToList();

    /// <summary> The tables that get a Blazor OData page: served, listed as a screen of the Blazor stack and given a client by <see cref="BlazorNames"/> (Blazor has no name/active or master-detail page). </summary>
    public static List<TableModel> BlazorScreenTables(DatabaseModel database, ProjectSettings project) =>
        database.ScreenTables(project, "Blazor").Where(t => Serves(t, project) && BlazorNames.HasClient(t, project)).ToList();

    /// <summary> The tables that get an Uno OData page: served, listed as a screen of the Uno stack and given a client by <see cref="UnoNames"/>. </summary>
    public static List<TableModel> UnoScreenTables(DatabaseModel database, ProjectSettings project) =>
        database.ScreenTables(project, "Uno").Where(t => Serves(t, project) && UnoNames.HasClient(t, project)).ToList();

    /// <summary> A table is probably served as OData, known from its name and the project's settings alone (the screen of another table cannot read its model): <c>OData=true</c>, the list names it or is blank, and none of
    /// the settings that refuse a table (the user table, <c>NoApiTables</c>, DTOs) names it. A table that only the model refuses (an owner column that is missing, editable child lists) is not known here. </summary>
    public static bool ServesByName(string table, ProjectSettings project) =>
        project.OData
        && (NamesAll(project) || project.ODataTables.Any(r => r.EqualsIgnoreCase(table)))
        && !(project.UserTable is { } user && user.EqualsIgnoreCase(table))
        && project.NoApi(table, new LookupShape(false, 0)) != true
        && !project.Dtos;

    /// <summary> The table's rows belong to a person (with <c>ApiAuth</c>): by an owner column of its own or through its parent (<see cref="OwnedTable"/>). The OData routes then scope its rows, its reads and its writes to the owners the
    /// caller may act for, exactly as the REST routes do, and no entity set has a navigation into it (an <c>$expand</c> or a sort through a parent would read another person's row). </summary>
    public static bool IsScoped(TableModel table, ProjectSettings project) => !table.IsNameActiveTable && OwnedTable.IsScoped(table, project);

    public static ODataSelection For(DatabaseModel database, ProjectSettings project)
    {
        string[] requested = project.ODataTables;
        bool all = NamesAll(project);
        var tables = new List<TableModel>();
        var refusals = new List<ODataRefusal>();
        var eligible = database.EntityTables.Where(t => HasApi(t, project)).ToList();

        foreach (var table in eligible)
        {
            if (!IsRequested(table, project))
                continue;
            if (RefusalOf(table, project) is { } reason)
                refusals.Add(new ODataRefusal(table.TableName, reason));
            else
                tables.Add(table);
        }

        if (!all)
        {
            foreach (string name in requested.Where(r => !eligible.Any(t => t.TableName.EqualsIgnoreCase(r))))
                refusals.Add(new ODataRefusal(name, database.Tables.Any(t => t.TableName.EqualsIgnoreCase(name)) ? NoCrudApi : "is not a table of this database"));
        }
        return new ODataSelection(tables, refusals);
    }

    /// <summary> Why the table is not served as OData, or null when it is. </summary>
    public static string? RefusalOf(TableModel table, ProjectSettings project)
    {
        if (project.UserTable is { } user && user.EqualsIgnoreCase(table.TableName))
            return "is the user table: its rows hold password hashes";
        if (project.NoApi(table.TableName, table.LookupShape) == true)
            return "is listed in NoApiTables";
        if (!table.IsNameActiveTable && OwnedTable.IsListedButUnusable(table, project))
            return "is listed in OwnedTables but has no NOT NULL whole-number owner column (OwnerColumn) and no owned parent, so its rows cannot be scoped to a person";
        if (EditableChild.Lists(table, project))
            return "has child lists that are edited inside its form (EditableChildren); the OData routes do not save them";
        if (DtoShape.Uses(table, project))
            return "uses Dtos: the REST API hides columns that the OData entity would show";
        return null;
    }
}
