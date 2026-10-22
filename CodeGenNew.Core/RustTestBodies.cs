namespace CodeGenNew.Core;

/// <summary> What the generated Rust integration tests (RS_Test, RS_TestBodies) decide about a column of a request body. </summary>
public static class RustTestBodies
{
    private static readonly string[] FixedFormatWords = ["Email", "Phone", "Fax", "Url", "Website"];

    /// <summary> A text value that is not a fixed format and not a key: it can take a unique ending, so a unique index does not stop a second run. </summary>
    public static bool IsFreeText(TableModel table, ColumnModel c) => c.IsStringColumn && !c.HasChoices && !c.IsPrimaryKey
        && !table.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0] == c.Name)
        && !FixedFormatWords.Any(word => c.Name.ContainsIgnoreCase(word));

    /// <summary> The foreign key a body column points through to a parent table that has a REST API: its table, or null (a composite or self reference, a key of another kind, a parent without an API). </summary>
    public static ForeignKeyModel? ParentKey(TableModel table, ColumnModel c, ProjectSettings project)
    {
        var fk = table.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0] == c.Name && f.ReferencedColumns.Count == 1);
        if (fk is null || fk.ReferencedTable == table.TableName || !(c.IsIntegerColumn || c.IsStringColumn || c.IsGuidColumn) || project.NoRepository(fk.ReferencedTable, fk.ReferencedLookupShape) == true)
            return null;
        return fk;
    }

    /// <summary> The list route of a table (<c>/api/customers</c>), as RustTable writes it. </summary>
    public static string Route(string tableName) => "/api/" + ScreenNames.BaseName(tableName).ToLowerInvariant().Pluralize();

    /// <summary> The role a test signs in as to be an ordinary person: the first role every list of the table's rules names besides the administrator's, any role when the rules name none, and null
    /// when the rules leave no such role (reading and writing belong to different roles). </summary>
    public static string? PlainRole(ProjectSettings project, IReadOnlyList<string> readRoles, IReadOnlyList<string> writeRoles)
    {
        var lists = new[] { readRoles, writeRoles }.Where(list => list.Count > 0).ToList();
        if (lists.Count == 0)
            return project.AuthAdminRole.EqualsIgnoreCase("Member") ? "Guest" : "Member";
        return lists[0].FirstOrDefault(role => !role.EqualsIgnoreCase(project.AuthAdminRole) && lists.All(list => list.Contains(role)));
    }

    /// <summary> Whether the junction routes of a many-to-many table get a test module: both sides have a REST API, whose rows the test links. </summary>
    public static bool JunctionTested(TableModel table, ProjectSettings project) =>
        project.RustTests && table.IsJunctionTable && table.Junction is { } j && HasApi(j.AnchorForeignKey, project) && HasApi(j.TargetForeignKey, project);

    private static bool HasApi(ForeignKeyModel fk, ProjectSettings project) => fk.ReferencedColumns.Count == 1 && !project.NoApiFor(fk.ReferencedTable, fk.ReferencedLookupShape);

    /// <summary> The tables whose junction routes get a test module, in name order. </summary>
    public static IReadOnlyList<TableModel> JunctionTables(DatabaseModel database, ProjectSettings project) =>
        [.. database.Tables.Where(t => JunctionTested(t, project)).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase)];

    /// <summary> The module of the junction test of a table (<c>tests/api/customer_tag_junction.rs</c>). </summary>
    public static string JunctionModule(TableModel table) => RustNames.Snake(table.TableName) + "_junction";

    /// <summary> The tests of the dashboard route are written when the project has a dashboard. </summary>
    public static bool HasDashboardTests(ProjectSettings project) => project.RustTests && project.Dashboard;

    /// <summary> The tests of the CSV routes are written when the project has them (ApiCsv) and a table has an API. </summary>
    public static bool HasCsvTests(DatabaseModel database, ProjectSettings project) => project.RustTests && project.ApiCsv && database.CrudApiTables(project).Count > 0;

    /// <summary> The tests of the gates and guards are written when the project names a rule that fits the schema. </summary>
    public static bool HasGateTests(DatabaseModel database, ProjectSettings project)
    {
        if (!project.RustTests || !project.ApiAuth)
            return false;
        var plan = GatePlan.Resolve(database, project);
        return plan.Problems.Count == 0 && (plan.Gates.Count > 0 || plan.Guards.Count > 0);
    }
}
