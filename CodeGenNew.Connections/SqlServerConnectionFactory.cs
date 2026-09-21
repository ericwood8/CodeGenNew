using Microsoft.Data.SqlClient;

namespace CodeGenNew.Connections;

public static class SqlServerConnectionFactory
{
    public static string BuildConnectionString(ConnectionRequest request)
    {
        if (request.Provider != DatabaseProvider.SqlServer)
            throw new NotSupportedException($"Provider '{request.Provider}' is not implemented yet. Only SqlServer is supported in v1.");

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

    public static SqlConnection CreateConnection(ConnectionRequest request) =>
        new(BuildConnectionString(request));

    public static async Task<bool> TestConnectionAsync(ConnectionRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(request);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
