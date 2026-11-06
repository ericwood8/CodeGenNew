namespace CodeGenNew.Core;

/// <summary> What the generated tests of the Python and Rust APIs need to know about a usage ledger (PY_TestUsage, RS_TestUsage): which ledgers they cover, how a test makes up a kind of its own, the statements
/// that take its events away again, and the sample units. The ASP.NET tests (<see cref="ApiUsageTests"/>) cover the same ledgers with the same cases. </summary>
public static class UsageTestPlan
{
    /// <summary> The ledgers the tests cover: those with a record route (the tests record their events through it) and a kind column that holds a made-up kind. Empty without a tenant API that signs in. </summary>
    public static IReadOnlyList<UsageShape> Ledgers(DatabaseModel database, ProjectSettings project) =>
        !project.ApiAuth || TenantPlan.TenantTableOf(database, project) is null ? [] :
        database.Tables.Select(t => TableShapes.For(t, project).Usage).OfType<UsageShape>().Where(t => UsageWriter.HasRecordRoute(t) && ApiUsageTests.KindLength(t) >= 4)
            .OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The characters of a made-up kind (a letter and random characters). </summary>
    public static int KindLength(UsageShape t) => ApiUsageTests.KindLength(t);

    /// <summary> The statement that removes the events of one tenant and kind, with the placeholders of the tenant and the kind. </summary>
    public static string RemoveEvents(DatabaseModel database, UsageShape t, string tenantPlaceholder, string kindPlaceholder)
    {
        var di = DialectInfo.For(database.Dialect);
        return $"DELETE FROM {di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName)} WHERE {di.Quote(t.Tenant.DbName)} = {tenantPlaceholder} AND {di.Quote(t.Kind.DbName)} = {kindPlaceholder}";
    }

    /// <summary> The statement that removes the monthly totals of one tenant and kind; null when the ledger has no monthly table. </summary>
    public static string? RemoveMonths(DatabaseModel database, UsageRollupShape? rollup, string tenantPlaceholder, string kindPlaceholder)
    {
        if (rollup is null)
            return null;
        var di = DialectInfo.For(database.Dialect);
        return $"DELETE FROM {di.QuoteTable(rollup.Table.SchemaName, rollup.Table.DbTableName)} WHERE {di.Quote(rollup.Tenant.DbName)} = {tenantPlaceholder} AND {di.Quote(rollup.Kind.DbName)} = {kindPlaceholder}";
    }

    /// <summary> The units of three events as JSON numbers and the sum of the first two: whole numbers for a whole-number ledger, fractions for a decimal one; empty for a ledger with no units column (each event is one unit). </summary>
    public static (string One, string Two, string Three, double SumOfTwo) Units(UsageShape t) =>
        t.Quantity is null ? ("", "", "", 2) : t.QuantityIsDecimal ? ("3.25", "4.5", "5.75", 7.75) : ("3", "4", "5", 7);

    /// <summary> What the plan limit tests of a ledger need: the first kind the plan limits in this ledger and the statements that make a plan of the tests' own (the same ones as the subscription tests). </summary>
    public sealed record LimitCase(string Key, SubscriptionTestSql Sql);

    /// <summary> The plan limit tests of a ledger, or null when it has none to play: a kind the plan limits, units to count, subscriptions on the stack (<paramref name="stackHasSubscriptions"/>) and a database the plan can be put in with SQL. </summary>
    public static LimitCase? LimitCaseOf(DatabaseModel database, ProjectSettings project, UsageShape t, bool stackHasSubscriptions, string tenantPlaceholder)
    {
        var keys = UsagePlan.KeysOf(database, project, t);
        if (!stackHasSubscriptions || t.Quantity is null || keys.Count == 0 || SubscriptionFeature.Problems(database, project).Count > 0)
            return null;
        return SubscriptionTestSql.For(database, project, tenantPlaceholder, out _) is { } sql ? new LimitCase(keys[0], sql) : null;
    }

    /// <summary> The statement that removes every event of a tenant, ending in the placeholder (or literal) the caller passes for the tenant. </summary>
    public static string RemoveTenantEvents(DatabaseModel database, UsageShape t)
    {
        var di = DialectInfo.For(database.Dialect);
        return $"DELETE FROM {di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName)} WHERE {di.Quote(t.Tenant.DbName)} = ";
    }

    /// <summary> The same for the monthly totals; null when the ledger has none. </summary>
    public static string? RemoveTenantMonths(DatabaseModel database, UsageRollupShape? rollup)
    {
        if (rollup is null)
            return null;
        var di = DialectInfo.For(database.Dialect);
        return $"DELETE FROM {di.QuoteTable(rollup.Table.SchemaName, rollup.Table.DbTableName)} WHERE {di.Quote(rollup.Tenant.DbName)} = ";
    }
}
