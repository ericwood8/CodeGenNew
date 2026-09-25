using Microsoft.Data.SqlClient;

namespace CodeGenNew.Connections;

public static class SqlServerConnectionFactory
{
    public static string BuildConnectionString(this ConnectionRequest request)
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

    public static SqlConnection CreateConnection(this ConnectionRequest request) =>
        new(request.BuildConnectionString());

    public static async Task<bool> TestConnectionAsync(this ConnectionRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = request.CreateConnection();
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
