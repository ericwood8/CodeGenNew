using System.Globalization;
using System.Text.Json;

namespace CodeGenNew.Core;

/// <summary> The statements the generated subscription tests of the Python and Rust APIs (PY_TestSubscriptions, RS_TestSubscriptions) run against the scratch database: a plan of their own with a fixed key far above the
/// usual ones, its limits, and the removal of what a test made (the tenant's subscription, its rows of the table the test writes to, the plan). A tenant is made and removed through the API, like the tenant tests do.
/// <c>ClearSubscription</c> and <c>ClearRows</c> end in the placeholder (or literal) the caller passed for the tenant's key. Not for SQL Server: its identity key cannot be given without a session setting. </summary>
public sealed record SubscriptionTestSql(long PlanKey, string AddPlan, IReadOnlyList<string> RemovePlan, Func<string, long?, IReadOnlyList<string>> SetLimit, string ClearSubscription, string? ClearRows,
    string? LimitKey, TableModel? WriteTable)
{
    public const long FirstKey = 90001;

    /// <summary> The statements, or null with the reason when the schema does not allow them (a required column of the plan table that has no example value, a key that is not a whole number). </summary>
    public static SubscriptionTestSql? For(DatabaseModel database, ProjectSettings project, string tenantPlaceholder, out string reason)
    {
        reason = "";
        if (database.Dialect == SqlDialect.SqlServer || SubscriptionFeature.SubscriptionOf(database, project) is not { } subscription)
        {
            reason = "no subscription table, or SQL Server";
            return null;
        }
        var limit = SubscriptionFeature.LimitTableOf(database, project);
        var di = DialectInfo.For(database.Dialect);
        string Name(TableModel t) => di.QuoteTable(t.SchemaName, t.DbTableName);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string key = FirstKey.ToString(CultureInfo.InvariantCulture);
        string? Literal(ColumnModel c, TableModel t)
        {
            string json = c.ValidExample(project, t.TableName);
            if (json == "null" || c.IsBinaryColumn)
                return null;
            if (json is "true" or "false")
                return di.BooleanLiteral(json == "true");
            if (json.StartsWith('"'))
                return "'" + JsonSerializer.Deserialize<string>(json)!.Replace("'", "''") + "'";
            return json;
        }

        // a statement that inserts one row: the key (always, so the tests know it), the values named, and a sample for every other column that cannot be NULL and has no default
        string? Insert(TableModel t, IReadOnlyDictionary<string, string> named, out string why)
        {
            why = "";
            var columns = new List<string>();
            var values = new List<string>();
            foreach (var c in t.Columns.OrderBy(c => c.OrdinalPosition))
            {
                string? value;
                if (named.TryGetValue(c.Name, out var given))
                    value = given;
                else if (c.IsPrimaryKey && c.IsIntegerColumn && t.PrimaryKeyColumns.Count == 1)
                    value = key;
                else if (c.IsComputed || c.IsRowVersionColumn || c.IsNullable || c.DatabaseDefaultSql is not null)
                    continue;
                else
                    value = Literal(c, t);
                if (value is null)
                {
                    why = $"{t.TableName}.{c.Name} has no example value";
                    return null;
                }
                columns.Add(Q(c));
                values.Add(value);
            }
            return $"INSERT INTO {Name(t)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
        }

        if (subscription.PlanTable.PrimaryKeyColumns.Count != 1 || !subscription.PlanKey.IsIntegerColumn)
        {
            reason = $"{subscription.PlanTable.TableName} needs a single whole-number key";
            return null;
        }
        var planNamed = new Dictionary<string, string>();
        if (subscription.PlanName is not null)
            planNamed[subscription.PlanName.Name] = "'zz plan'";
        if (Insert(subscription.PlanTable, planNamed, out reason) is not { } addPlan)
            return null;

        var removePlan = new List<string>();
        if (limit is not null)
            removePlan.Add($"DELETE FROM {Name(limit.Table)} WHERE {Q(limit.Plan)} = {key}");
        removePlan.Add($"DELETE FROM {Name(subscription.PlanTable)} WHERE {Q(subscription.PlanKey)} = {key}");

        Func<string, long?, IReadOnlyList<string>> setLimit = (_, _) => [];
        if (limit is not null)
        {
            setLimit = (name, value) =>
            {
                var named = new Dictionary<string, string>
                {
                    [limit.Plan.Name] = key, [limit.Key.Name] = $"'{name}'", [limit.Value.Name] = value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "NULL"
                };
                var statements = new List<string> { $"DELETE FROM {Name(limit.Table)} WHERE {Q(limit.Plan)} = {key} AND {Q(limit.Key)} = '{name}'" };
                if (Insert(limit.Table, named, out _) is { } insert)
                    statements.Add(insert);
                return statements;
            };
        }

        var write = SubscriptionFeature.TestWriteTable(database, project);
        var limited = write is null || limit is null ? null : SubscriptionFeature.LimitedTables(database, project).FirstOrDefault(l => l.Table == write.Table && !l.IsMembership);
        string? clearRows = write is null ? null : $"DELETE FROM {Name(write.Table)} WHERE {Q(write.Column)} = {tenantPlaceholder}";
        string clearSubscription = $"DELETE FROM {Name(subscription.Table)} WHERE {Q(subscription.Tenant)} = {tenantPlaceholder}";
        return new SubscriptionTestSql(FirstKey, addPlan, removePlan, setLimit, clearSubscription, clearRows, limited?.LimitKey, write?.Table);
    }
}
