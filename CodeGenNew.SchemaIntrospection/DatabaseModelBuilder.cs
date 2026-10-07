using CodeGenNew.Core;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds the <see cref="DatabaseModel"/> a database-level template receives: every table of one schema, each read like a
/// single-table template's table (so the lookup shape, naming style and child tables are all there). </summary>
public static class DatabaseModelBuilder
{
    /// <summary> Tables read at once. Each read opens its own pooled connection, so this also caps the connections used. </summary>
    private const int MaxParallelReads = 4;

    public static async Task<DatabaseModel> BuildAsync(
        this ISchemaProvider provider, string databaseName, string schemaName, CancellationToken cancellationToken = default)
    {
        var summaries = (await provider.ListTablesAsync(cancellationToken))
            .Where(s => s.SchemaName.EqualsIgnoreCase(schemaName))
            .ToList();

        var tables = new TableModel[summaries.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, summaries.Count),
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelReads, CancellationToken = cancellationToken },
            async (i, token) => tables[i] = await provider.BuildTableModelAsync(summaries[i].SchemaName, summaries[i].TableName, cancellationToken: token));

        return new DatabaseModel
        {
            DatabaseName = databaseName,
            SchemaName = schemaName,
            Dialect = tables.Length > 0 ? tables[0].Dialect : SqlDialect.SqlServer,
            Tables = [.. tables]
        };
    }
}
