using CodeGenNew.Core;
using CodeGenNew.Connections;

namespace CodeGenNew.SchemaIntrospection;

public static class SchemaProviderFactory
{
    public static ISchemaProvider Create(ConnectionRequest request, string specialLogicColumnsConfigPath, NamingStyle naming = NamingStyle.AsIs, IReadOnlyCollection<string>? acronyms = null) => request.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlServerSchemaProvider(request, specialLogicColumnsConfigPath, naming, acronyms),
        DatabaseProvider.PostgreSql => new PostgresSchemaProvider(request, specialLogicColumnsConfigPath, naming, acronyms),
        DatabaseProvider.MySql => new MySqlSchemaProvider(request, specialLogicColumnsConfigPath, naming, acronyms),
        _ => throw new NotSupportedException($"Provider '{request.Provider}' is not implemented yet. SqlServer, PostgreSql and MySql are supported.")
    };
}
