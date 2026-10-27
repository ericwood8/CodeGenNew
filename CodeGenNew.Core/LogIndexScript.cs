using System.Text;

namespace CodeGenNew.Core;

/// <summary> The script that gives each log table an index on its timestamp, so a date range and the newest-first page read the end of the index and never scan the table. A table that already has
/// an index starting with the timestamp gets none. Written for the project to review and run; the generator never applies it. </summary>
public static class LogIndexScript
{
    public static string IndexName(TableModel table, LogTable log) => $"IX_{table.TableName}_{log.Timestamp.DbName}";

    public static IReadOnlyList<LogTable> Missing(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => LogTablePlan.For(t, project)).OfType<LogTable>()
            .Where(log => !log.Table.IsIndexed([log.Timestamp.DbName]))
            .OrderBy(log => log.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string Text(DatabaseModel database, ProjectSettings project)
    {
        var dialect = DialectInfo.For(database.Dialect);
        var o = new StringBuilder();
        foreach (var log in Missing(database, project))
        {
            string table = dialect.QuoteTable(log.Table.SchemaName, log.Table.DbTableName);
            string column = dialect.Quote(log.Timestamp.DbName);
            string name = dialect.Quote(IndexName(log.Table, log));
            o.Append($"-- {log.Table.TableName}: a date range and the newest-first page read the end of this index\n");
            o.Append(database.Dialect switch
            {
                SqlDialect.SqlServer => $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{IndexName(log.Table, log)}' AND object_id = OBJECT_ID(N'{log.Table.SchemaName}.{log.Table.DbTableName}'))\n    CREATE INDEX {name} ON {table} ({column} DESC);\n",
                SqlDialect.MySql => $"CREATE INDEX {name} ON {table} ({column});\n",
                _ => $"CREATE INDEX IF NOT EXISTS {name} ON {table} ({column} DESC);\n"
            });
            o.Append('\n');
        }
        return o.ToString();
    }
}
