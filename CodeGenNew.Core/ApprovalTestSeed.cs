using System.Globalization;
using System.Text.Json;

namespace CodeGenNew.Core;

/// <summary> The rows the generated approval tests of the Python and Rust APIs (PY_TestApprovals, RS_TestApprovals) put in a scratch copy of the database, and the statements that take them out again: the people of the
/// user table (a requester and two approvers, with <c>ApproverTable</c> also a person approved for another table only), the approver rows, and one row of the first table of <c>RowApprovals</c>. The keys are fixed
/// numbers far above the usual ones, so a second run first clears what the first left. </summary>
public sealed record ApprovalTestSeed(ApprovalTable Table, IReadOnlyList<string> Setup, IReadOnlyList<string> Cleanup, long RowKey)
{
    public const string Requester = "zz_ann";
    public const string Approver = "zz_bob";
    public const string AnyTableApprover = "zz_cy";
    public const string OtherTableApprover = "zz_dee";
    public const string OtherTable = "Elsewhere";

    private const long FirstKey = 90001;

    /// <summary> Whether the people are approved by rows of <c>ApproverTable</c> that name a table (so a person approved for another table only can be seeded). </summary>
    public static bool HasOtherTablePerson(ApprovalPlan plan) => plan.Approvers?.ForTable is not null;

    /// <summary> The seed, or null with the reason when it cannot be made from the schema (a required column that has no example value, a key that is not a whole number). </summary>
    public static ApprovalTestSeed? For(ApprovalPlan plan, DatabaseModel database, ProjectSettings project, out string reason)
    {
        reason = "";
        if (plan.Tables.Count == 0 || plan.User is not { } user)
        {
            reason = "no table has RowApprovals";
            return null;
        }
        var di = DialectInfo.For(database.Dialect);
        var table = plan.Tables[0];
        var userKey = user.Table.PrimaryKeyColumns[0];
        if (user.Table.PrimaryKeyColumns.Count != 1 || !userKey.IsIntegerColumn)
        {
            reason = $"{user.Table.TableName} needs a single whole-number key";
            return null;
        }

        string Name(TableModel t) => di.QuoteTable(t.SchemaName, t.DbTableName);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Bool(bool value) => database.Dialect == SqlDialect.PostgreSql ? (value ? "true" : "false") : value ? "1" : "0";
        string? Literal(ColumnModel c, TableModel t)
        {
            string json = ApiSampleValues.Value(c, project, t.TableName);
            if (json == "null" || c.SqlType is System.Data.SqlDbType.Binary or System.Data.SqlDbType.VarBinary or System.Data.SqlDbType.Image)
                return null;
            if (json is "true" or "false")
                return Bool(json == "true");
            if (json.StartsWith('"'))
                return "'" + JsonSerializer.Deserialize<string>(json)!.Replace("'", "''") + "'";
            return json;
        }

        // a statement that inserts one row: the key, the values named, and a sample for every other column that cannot be NULL and has no default
        string? Insert(TableModel t, IReadOnlyDictionary<string, string> named, string key, out string why)
        {
            why = "";
            var columns = new List<string>();
            var values = new List<string>();
            foreach (var c in t.Columns.OrderBy(c => c.OrdinalPosition))
            {
                string? value = null;
                if (c.IsPrimaryKey)
                    value = key;
                else if (named.TryGetValue(c.Name, out var given))
                    value = given;
                else if (c.IsComputed || c.IsRowVersionColumn || c.IsNullable || c.DatabaseDefaultSql is not null)
                    continue;
                else if (t.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase)) is { } fk)
                {
                    var parent = database.Tables.FirstOrDefault(p => p.TableName.EqualsIgnoreCase(fk.ReferencedTable));
                    if (parent is null || fk.ReferencingColumns.Count != 1 || fk.ReferencedColumns.Count != 1)
                    {
                        why = $"{t.TableName}.{c.Name} points at a table the test cannot fill";
                        return null;
                    }
                    var parentKey = parent.Columns.First(p => p.Name.EqualsIgnoreCase(fk.ReferencedColumns[0]));
                    value = $"(SELECT MIN({Q(parentKey)}) FROM {Name(parent)})";
                }
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

        var setup = new List<string>();
        var cleanup = new List<string>();
        var people = new List<(string Login, long Key, bool Manager)> { (Requester, FirstKey, false), (Approver, FirstKey + 1, true), (AnyTableApprover, FirstKey + 2, true) };
        if (HasOtherTablePerson(plan))
            people.Add((OtherTableApprover, FirstKey + 3, false));

        foreach (var (login, key, manager) in people)
        {
            var named = new Dictionary<string, string> { [user.UserName.Name] = "'" + login + "'", [user.PasswordHash.Name] = "'x'" };
            if (user.Role is { } role && role.IsBooleanColumn)
                named[role.Name] = Bool(manager && plan.Approvers is null);
            if (user.Admin is { } admin)
                named[admin.Name] = Bool(false);
            if (Insert(user.Table, named, key.ToString(CultureInfo.InvariantCulture), out reason) is not { } sql)
                return null;
            setup.Add(sql);
        }

        if (plan.Approvers is { } approvers)
        {
            var rows = new List<(long Person, string? Table)> { (FirstKey + 1, table.Table.TableName), (FirstKey + 2, null) };
            if (HasOtherTablePerson(plan))
                rows.Add((FirstKey + 3, OtherTable));
            long approverKey = FirstKey;
            foreach (var (person, forTable) in rows)
            {
                var named = new Dictionary<string, string> { [approvers.Person.Name] = person.ToString(CultureInfo.InvariantCulture) };
                if (approvers.ForTable is { } forColumn)
                    named[forColumn.Name] = forTable is null ? "NULL" : "'" + forTable + "'";
                if (Insert(approvers.Table, named, (approverKey++).ToString(CultureInfo.InvariantCulture), out reason) is not { } sql)
                    return null;
                setup.Add(sql);
            }
            cleanup.Add($"DELETE FROM {Name(approvers.Table)} WHERE {Q(approvers.Table.PrimaryKeyColumns[0])} >= {FirstKey} AND {Q(approvers.Table.PrimaryKeyColumns[0])} < {approverKey}");
        }

        var rowNamed = new Dictionary<string, string>();
        foreach (var fk in table.Table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && f.ReferencedTable.EqualsIgnoreCase(user.Table.TableName)))
            rowNamed[fk.ReferencingColumns[0]] = FirstKey.ToString(CultureInfo.InvariantCulture);
        if (Insert(table.Table, rowNamed, FirstKey.ToString(CultureInfo.InvariantCulture), out reason) is not { } rowSql)
            return null;
        setup.Add(rowSql);

        cleanup.Insert(0, $"DELETE FROM {Name(table.Side)} WHERE {Q(table.Link)} = {FirstKey}");
        cleanup.Insert(1, $"DELETE FROM {Name(table.Table)} WHERE {Q(table.Key)} = {FirstKey}");
        cleanup.Add($"DELETE FROM {Name(user.Table)} WHERE {Q(userKey)} >= {FirstKey} AND {Q(userKey)} < {FirstKey + people.Count}");
        return new ApprovalTestSeed(table, setup, cleanup, FirstKey);
    }
}
