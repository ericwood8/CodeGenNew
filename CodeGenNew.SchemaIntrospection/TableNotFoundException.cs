namespace CodeGenNew.SchemaIntrospection;

/// <summary> The table asked for has no columns in the catalog: it does not exist under that name in that schema. Not a transient failure, so nothing retries it. On a server
/// whose table names are case-sensitive (MySQL on Linux) the usual cause is a name in the wrong case (<c>MOVIES</c> for <c>Movies</c>). </summary>
public sealed class TableNotFoundException(string schemaName, string tableName) : Exception(
    $"Table [{schemaName}].[{tableName}] was not found. Check the spelling; on a server with case-sensitive table names (MySQL on Linux) the case must match exactly.")
{
    public string SchemaName { get; } = schemaName;
    public string TableName { get; } = tableName;
}
