using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace CodeGenNew.Connections;

public static class SqlServerConnectionFactory
{
    public static string BuildConnectionString(this ConnectionRequest request)
    {
        if (request.Provider == DatabaseProvider.PostgreSql)
            return request.BuildPostgresConnectionString();
        if (request.Provider == DatabaseProvider.MySql)
            return request.BuildMySqlConnectionString();

        if (request.Provider != DatabaseProvider.SqlServer)
            throw new NotSupportedException($"Provider '{request.Provider}' is not implemented yet. SqlServer, PostgreSql and MySql are supported.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = request.ServerName,
            InitialCatalog = request.DatabaseName,
            TrustServerCertificate = request.TrustServerCertificate
        };

        if (request.AuthMode == AuthMode.WindowsAuth)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
                throw new ArgumentException("UserName is required for SQL Login authentication.", nameof(request));

            builder.UserID = request.UserName;
            builder.Password = request.Password ?? "";
        }

        return builder.ConnectionString;
    }

    public static SqlConnection CreateConnection(this ConnectionRequest request) =>
        new(request.BuildConnectionString());

    /// <summary> A connection of whichever database the request names (SQL Server, PostgreSQL or MySQL). </summary>
    public static DbConnection CreateDbConnection(this ConnectionRequest request) => request.Provider switch
    {
        DatabaseProvider.PostgreSql => request.CreatePostgresConnection(),
        DatabaseProvider.MySql => request.CreateMySqlConnection(),
        _ => request.CreateConnection()
    };

    public static async Task<bool> TestConnectionAsync(this ConnectionRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = request.CreateDbConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }
}
