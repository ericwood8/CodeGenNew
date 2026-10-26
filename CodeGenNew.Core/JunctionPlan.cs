namespace CodeGenNew.Core;

/// <summary> How a front end reaches the editor of a many-to-many junction table: a menu entry whose page picks the anchor's row and shows the two-list editor. Every stack names the route, the label and the
/// roles from here, and lists the same tables, so the menu entries cannot drift apart. </summary>
public static class JunctionPlan
{
    /// <summary> The route of a junction table's page ("ItemWarehouse" is "item-warehouse-links"). </summary>
    public static string Route(string table) => ScreenNames.Route(table) + "-links";

    /// <summary> The menu label of a junction table's page ("Item Warehouse Links"). </summary>
    public static string Label(string table) => ScreenNames.Label(table) + " Links";

    /// <summary> Whether the junction table gets a page: it is a many-to-many junction table and its anchor table has a lookup endpoint to pick a row from. </summary>
    public static bool HasPage(TableModel table, ProjectSettings project) => table.IsJunctionTable && table.Junction!.AnchorHasLookup(project);

    /// <summary> The junction tables that get a page, in menu order (by name). </summary>
    public static List<TableModel> Tables(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Where(t => HasPage(t, project)).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The roles that may open the page: with <c>Auth=true</c> the roles that may read the junction table, else none are asked. </summary>
    public static IReadOnlyList<string> Roles(ProjectSettings project, TableModel table) => project.Auth ? WriteRolePlan.ReadFor(project, table) : [];

    /// <summary> The heading of the page: the target's rows by the anchor's row ("Warehouses by Item"). </summary>
    public static string Heading(TableModel table) =>
        Labels.Words(BlazorNames.TypeName(table.Junction!.TargetRole)).Pluralize() + " by " + Labels.Words(BlazorNames.TypeName(table.Junction!.AnchorRole));

    /// <summary> The React page of a junction table: <c>ItemWarehouseLinksPage</c>. </summary>
    public static string ReactPage(string table) => ScreenNames.BaseName(table) + "LinksPage";

    /// <summary> The Angular component of a junction table's page (<c>ItemWarehouseLinksComponent</c>) and its folder and file stem (<c>item-warehouse-links</c>). </summary>
    public static string AngularClass(string table) => ScreenNames.BaseName(table) + "LinksComponent";
    public static string AngularStem(string table) => ScreenNames.Stem(table) + "-links";

    /// <summary> What a web page needs to read the anchor's rows: the lookup route (<c>/api/items/lookup</c>) of the anchor table and the TypeScript type of its key. </summary>
    public static string AnchorLookupRoute(TableModel table) => BlazorNames.ApiRoute(table.Junction!.AnchorRole) + "/lookup";
}
