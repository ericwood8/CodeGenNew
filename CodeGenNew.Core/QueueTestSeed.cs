using System.Globalization;
using System.Text.Json;

namespace CodeGenNew.Core;

/// <summary> The rows the generated queue tests of the Python and Rust APIs (PY_TestQueue, RS_TestQueue) put in a scratch copy of the database and the statements that take them out again: three pending rows with fixed
/// keys far above the usual ones, created in the year 2000 so they are the oldest pending rows of the table whatever else it holds, and the statement that makes a claim stale. </summary>
public sealed record QueueTestSeed(QueueTable Queue, IReadOnlyList<string> Setup, IReadOnlyList<string> Cleanup, IReadOnlyList<long> Keys, string StaleSql)
{
    private const long FirstKey = 90001;
    private const int Rows = 3;

    /// <summary> The seed, or null with the reason when it cannot be made from the schema (a required column that has no example value, a key that is not a whole number). </summary>
    public static QueueTestSeed? For(QueueTable queue, DatabaseModel database, ProjectSettings project, out string reason)
    {
        reason = "";
        var di = DialectInfo.For(database.Dialect);
        var table = queue.Table;
        string name = di.QuoteTable(table.SchemaName, table.DbTableName);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        var keys = Enumerable.Range(0, Rows).Select(i => FirstKey + i).ToList();
        var setup = new List<string>();
        foreach (var (key, i) in keys.Select((k, i) => (k, i)))
        {
            var columns = new List<string>();
            var values = new List<string>();
            foreach (var c in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                string? value;
                if (c == queue.Key)
                    value = key.ToString(CultureInfo.InvariantCulture);
                else if (c == queue.Status)
                    value = $"'{QueueStatus.Pending}'";
                else if (c == queue.Retry)
                    value = "0";
                else if (c == queue.Created)
                    value = $"'2000-01-01 00:00:0{i + 1}'";
                else if (c.IsComputed || c.IsRowVersionColumn || c.IsNullable || c.DatabaseDefaultSql is not null)
                    continue;
                else
                {
                    string json = ApiSampleValues.Value(c, project, table.TableName);
                    value = json switch
                    {
                        "null" => null,
                        "true" or "false" => database.Dialect == SqlDialect.PostgreSql ? json : json == "true" ? "1" : "0",
                        _ when json.StartsWith('"') => "'" + JsonSerializer.Deserialize<string>(json)!.Replace("'", "''") + "'",
                        _ => json
                    };
                }
                if (value is null)
                {
                    reason = $"{table.TableName}.{c.Name} has no example value";
                    return null;
                }
                columns.Add(Q(c));
                values.Add(value);
            }
            setup.Add($"INSERT INTO {name} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})");
        }
        string range = $"{Q(queue.Key)} >= {keys[0]} AND {Q(queue.Key)} <= {keys[^1]}";
        var cleanup = new List<string> { $"DELETE FROM {name} WHERE {range}" };
        string stale = $"UPDATE {name} SET {Q(queue.Claimed)} = '2000-01-01 00:00:00' WHERE {range} AND {Q(queue.Status)} = '{QueueStatus.Claimed}'";
        return new QueueTestSeed(queue, setup, cleanup, keys, stale);
    }
}
