using System.Text;

namespace CodeGenNew.Core;

/// <summary> A unique index of a tenant-owned table that does not name the tenant column, so two tenants cannot hold the same value: <c>UNIQUE (Name)</c> refuses the second tenant's equal name. </summary>
public sealed record TenantIndexFix(TableModel Table, IndexModel Index, string NewName, IReadOnlyList<string> NewColumns);

/// <summary> The script that makes such an index per tenant, <c>(TenantId, Name)</c> (SP_TenantIndex). Nothing is applied: the script is reviewed and run by the person who owns the database. </summary>
public static class TenantIndexPlan
{
    private const int MaxNameLength = 60;

    /// <summary> The unique indexes of the tenant-owned tables that leave the tenant column out, in table and index name order. The primary key and the tenant table itself are not touched. </summary>
    public static IReadOnlyList<TenantIndexFix> Fixes(DatabaseModel database, ProjectSettings project)
    {
        var fixes = new List<TenantIndexFix>();
        if (TenantPlan.TenantTableOf(database, project) is null)
            return fixes;
        foreach (var owned in TenantPlan.OwnedTables(database, project))
        {
            string tenant = owned.Column.DbName;
            var key = owned.Table.PrimaryKeyColumns.Select(c => c.DbName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var index in owned.Table.Indexes.Where(i => i.IsUnique).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (index.Columns.Any(c => c.EqualsIgnoreCase(tenant)) || (key.Count > 0 && key.SetEquals(index.Columns)))
                    continue;
                string name = $"UX_{owned.Table.DbTableName}_{tenant}_{string.Join("_", index.Columns)}";
                fixes.Add(new TenantIndexFix(owned.Table, index, name.Length > MaxNameLength ? name[..MaxNameLength] : name, [tenant, .. index.Columns]));
            }
        }
        return fixes;
    }

    /// <summary> The script, or empty when no index needs the change. </summary>
    public static string Script(DatabaseModel database, ProjectSettings project)
    {
        var fixes = Fixes(database, project);
        if (fixes.Count == 0)
            return "";
        var di = DialectInfo.For(database.Dialect);
        var o = new StringBuilder();
        o.Append("-- Each unique index below leaves the tenant out, so a value taken by one tenant is refused to every other. Each becomes the same index per tenant.\n");
        o.Append("-- Review it, try it on a copy and run it yourself: codegen never applies it. A duplicate value inside one tenant stops the new index; the old one is dropped first.\n");
        foreach (var fix in fixes)
        {
            string table = di.QuoteTable(fix.Table.SchemaName, fix.Table.DbTableName);
            string created = $"CREATE UNIQUE INDEX {di.Quote(fix.NewName)} ON {table} ({string.Join(", ", fix.NewColumns.Select(di.Quote))});";
            o.Append($"\n-- {fix.Table.TableName}: {fix.Index.Name} on ({string.Join(", ", fix.Index.Columns)}) becomes ({string.Join(", ", fix.NewColumns)})\n");
            o.Append(Drop(database.Dialect, di, fix, table));
            o.Append(created).Append('\n');
        }
        return o.ToString();
    }

    private static string Drop(SqlDialect dialect, DialectInfo di, TenantIndexFix fix, string table)
    {
        string name = di.Quote(fix.Index.Name);
        string literal = "N'" + fix.Index.Name.Replace("'", "''") + "'";
        switch (dialect)
        {
            case SqlDialect.SqlServer:
                string tableLiteral = "N'" + table.Replace("'", "''") + "'";
                return $"IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = {literal} AND parent_object_id = OBJECT_ID({tableLiteral}))\n    ALTER TABLE {table} DROP CONSTRAINT {name};\n"
                    + $"ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = {literal} AND object_id = OBJECT_ID({tableLiteral}))\n    DROP INDEX {name} ON {table};\n";
            case SqlDialect.PostgreSql:
                return $"ALTER TABLE {table} DROP CONSTRAINT IF EXISTS {name};\nDROP INDEX IF EXISTS {di.QuoteTable(fix.Table.SchemaName, fix.Index.Name)};\n";
            case SqlDialect.MySql:
                return $"DROP INDEX {name} ON {table};\n";
            default:
                return fix.Index.Name.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase)
                    ? $"-- {fix.Index.Name} comes from a UNIQUE constraint in the table definition, which SQLite cannot drop: rebuild the table without it.\n"
                    : $"DROP INDEX IF EXISTS {name};\n";
        }
    }
}
