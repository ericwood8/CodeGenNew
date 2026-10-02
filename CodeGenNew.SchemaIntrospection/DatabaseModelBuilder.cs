using CodeGenNew.Core;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds the <see cref="DatabaseModel"/> a database-level template receives: every table of one schema, each read like a
/// single-table template's table (so the lookup shape, naming style and child tables are all there). </summary>
public static class DatabaseModelBuilder
{
    public static async Task<DatabaseModel> BuildAsync(
        this ISchemaProvider provider, string databaseName, string schemaName, CancellationToken cancellationToken = default)
    {
        var summaries = await provider.ListTablesAsync(cancellationToken);
        var tables = new List<TableModel>();
        foreach (var summary in summaries.Where(s => s.SchemaName.Equals(schemaName, StringComparison.OrdinalIgnoreCase)))
            tables.Add(await provider.BuildTableModelAsync(summary.SchemaName, summary.TableName, cancellationToken: cancellationToken));

        return new DatabaseModel
        {
            DatabaseName = databaseName,
            SchemaName = schemaName,
            Dialect = tables.Count > 0 ? tables[0].Dialect : SqlDialect.SqlServer,
            Tables = tables
        };
    }
}
