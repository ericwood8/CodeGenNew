using CodeGenNew.Core;
using CodeGenNew.Connections;

namespace CodeGenNew.SchemaIntrospection;

public static class SchemaProviderFactory
{
    public static ISchemaProvider Create(ConnectionRequest request, string specialLogicColumnsConfigPath, NamingStyle naming = NamingStyle.AsIs, IReadOnlyCollection<string>? acronyms = null, IReadOnlyCollection<string>? ignoredColumns = null)
    {
        SchemaProviderBase provider = request.Provider switch
        {
            DatabaseProvider.SqlServer => new SqlServerSchemaProvider(request, specialLogicColumnsConfigPath, naming, acronyms),
            DatabaseProvider.PostgreSql => new PostgresSchemaProvider(request, specialLogicColumnsConfigPath, naming, acronyms),
            DatabaseProvider.MySql => new MySqlSchemaProvider(request, specialLogicColumnsConfigPath, naming, acronyms),
            DatabaseProvider.Sqlite => new SqliteSchemaProvider(request, specialLogicColumnsConfigPath, naming, acronyms),
            _ => throw new NotSupportedException($"Provider '{request.Provider}' is not implemented yet. SqlServer, PostgreSql, MySql and Sqlite are supported.")
        };
        provider.IgnoredColumns = ignoredColumns;
        return provider;
    }
}
