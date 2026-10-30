namespace CodeGenNew.Core;

/// <summary> What the generated tests of the Python and Rust APIs of a multi-tenant project (PY_TestTenants, RS_Test, RS_TestTenants) decide the same way: which tables they exercise, the first two active tenants of
/// the scratch database they work in, and the body of a request that adds a row. The words are <see cref="TenantWords"/> and <see cref="NameActiveWords"/>, as in the ASP.NET tests (<see cref="TenantTests"/>). </summary>
public static class TenantTestPlan
{
    public enum BodyKind { Plain, Text, Parent }

    /// <summary> One member of a request body: a literal (<c>Plain</c>), text that takes a unique ending so a unique index does not stop a second run (<c>Text</c>, within <c>Length</c>), or the key of a row of the parent's API (<c>Parent</c>). </summary>
    public sealed record BodyMember(string Json, BodyKind Kind, string Value, int Length, string? ParentRoute, string? ParentKey);

    /// <summary> Whether the tests are written: the stack's tests are on and the API has tenancy. </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project, bool stackTests) =>
        stackTests && TenantPlan.HasApiTenancy(project) && TenantPlan.TenantTableOf(database, project) is not null;

    /// <summary> The tenant-owned tables with a plain CRUD API, in name order. </summary>
    public static IReadOnlyList<TenantOwned> Owned(DatabaseModel database, ProjectSettings project) =>
        [.. TenantPlan.OwnedTables(database, project).Where(o => NameActivePlan.For(o.Table, project) is null && o.Table.HasCrudApiOfAnyKey(project) && o.Table.PrimaryKeyColumns.Count == 1)];

    /// <summary> The tenant-owned tables with a Name and an IsActive column, in name order. </summary>
    public static IReadOnlyList<TenantOwned> OwnedNamed(DatabaseModel database, ProjectSettings project) =>
        [.. TenantPlan.OwnedTables(database, project).Where(o => NameActivePlan.For(o.Table, project) is not null && o.Table.PrimaryKeyColumns.Count == 1)];

    /// <summary> The key of the tenant table, which the tests need as a whole number. </summary>
    public static ColumnModel TenantKey(DatabaseModel database, ProjectSettings project) => TenantPlan.KeyOf(TenantPlan.TenantTableOf(database, project)!)!;

    /// <summary> The SQL that lists the keys of the first two active tenants, in key order. </summary>
    public static string FirstTenantsSql(DatabaseModel database, ProjectSettings project)
    {
        var table = TenantPlan.TenantTableOf(database, project)!;
        var di = DialectInfo.For(table.Dialect);
        string key = di.Quote(TenantKey(database, project).DbName);
        string active = table.IsActiveColumn is { } flag ? $" WHERE {di.Quote(flag.DbName)} = {di.BooleanLiteral(true)}" : "";
        return $"SELECT {key} FROM {di.QuoteTable(table.SchemaName, table.DbTableName)}{active} ORDER BY {key} LIMIT 2";
    }

    /// <summary> The JSON name of the active flag of the tenant table, or null when it has none (a token then never outlives its tenant, so there is no switch-off to play). </summary>
    public static string? ActiveKey(DatabaseModel database, ProjectSettings project) =>
        TenantPlan.TenantTableOf(database, project)?.IsActiveColumn is { } flag ? JsonNames.Camel(flag.Name) : null;

    /// <summary> The SQL that removes a tenant the tests made; <paramref name="key"/> is the placeholder or the literal of its key. </summary>
    public static string RemoveSql(DatabaseModel database, ProjectSettings project, string key)
    {
        var table = TenantPlan.TenantTableOf(database, project)!;
        var di = DialectInfo.For(table.Dialect);
        return $"DELETE FROM {di.QuoteTable(table.SchemaName, table.DbTableName)} WHERE {di.Quote(TenantKey(database, project).DbName)} = {key}";
    }

    /// <summary> Whether the tests play the first administrator as a new person. </summary>
    public static bool NewAdmin(DatabaseModel database, ProjectSettings project) => TenantPlan.OnboardsNewAdmin(database, project);

    /// <summary> The SQL that removes the membership rows of a tenant, or empty when the project has no membership table. </summary>
    public static string RemoveMembersSql(DatabaseModel database, ProjectSettings project, string key)
    {
        if (TenantPlan.MembershipOf(database, project) is not { } membership)
            return "";
        var di = DialectInfo.For(membership.Table.Dialect);
        return $"DELETE FROM {di.QuoteTable(membership.Table.SchemaName, membership.Table.DbTableName)} WHERE {di.Quote(membership.Tenant.DbName)} = {key}";
    }

    /// <summary> The SQL that removes the person with a user name; <paramref name="name"/> is the placeholder or the text literal. </summary>
    public static string RemovePersonSql(DatabaseModel database, ProjectSettings project, string name)
    {
        var plan = UserTable.Resolve(database, project).Plan!;
        var di = DialectInfo.For(plan.Table.Dialect);
        return $"DELETE FROM {di.QuoteTable(plan.Table.SchemaName, plan.Table.DbTableName)} WHERE {di.Quote(plan.UserName.DbName)} = {name}";
    }

    /// <summary> The JSON name of the user name column of the user table. </summary>
    public static string LoginKey(DatabaseModel database, ProjectSettings project) => JsonNames.Camel(UserTable.Resolve(database, project).Plan!.UserName.Name);

    /// <summary> The SQL that switches a tenant off, as the person who runs the tenants would; empty when the tenant table has no active flag. </summary>
    public static string SwitchOffSql(DatabaseModel database, ProjectSettings project, string key)
    {
        var table = TenantPlan.TenantTableOf(database, project)!;
        if (table.IsActiveColumn is not { } flag)
            return "";
        var di = DialectInfo.For(table.Dialect);
        return $"UPDATE {di.QuoteTable(table.SchemaName, table.DbTableName)} SET {di.Quote(flag.DbName)} = {di.BooleanLiteral(false)} WHERE {di.Quote(TenantKey(database, project).DbName)} = {key}";
    }

    /// <summary> The members of the body that adds a row: the identity key is left out, text takes a unique ending, a foreign key to a parent with an API points at a row of it. </summary>
    public static IReadOnlyList<BodyMember> Body(TableModel table, ProjectSettings project)
    {
        var members = new List<BodyMember>();
        foreach (var c in ApiSampleValues.BodyColumns(table))
        {
            string json = JsonNames.Camel(c.Name), sample = ApiSampleValues.Value(c, project, table.TableName);
            if (RustTestBodies.IsFreeText(table, c, project))
                members.Add(new BodyMember(json, BodyKind.Text, ApiSampleValues.Text(c, project, table.TableName), Math.Max(c.CharacterLength, 0), null, null));
            else if (RustTestBodies.ParentKey(table, c, project) is { } fk)
                members.Add(new BodyMember(json, BodyKind.Parent, sample, 0, RustTestBodies.Route(fk.ReferencedTable), JsonNames.Camel(fk.ReferencedColumns[0])));
            else
                members.Add(new BodyMember(json, BodyKind.Plain, sample, 0, null, null));
        }
        return members;
    }
}
