using Npgsql;

namespace CodeGenNew.Connections;

public static class PostgresConnectionFactory
{
    /// <summary> ServerName is "host" or "host:port" (the default port is 5432). PostgreSQL has no Windows-authentication
    /// mode that works across platforms, so a user name (and normally a password) is always required. </summary>
    public static string BuildPostgresConnectionString(this ConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            throw new ArgumentException("UserName is required for PostgreSQL.", nameof(request));

        string host = request.ServerName;
        int port = 5432;
        int colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host[(colon + 1)..], out int parsedPort))
        {
            port = parsedPort;
            host = host[..colon];
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = request.DatabaseName,
            Username = request.UserName,
            Password = request.Password ?? "",
            Timeout = 10,
            CommandTimeout = 30
        }.ConnectionString;
    }

    public static NpgsqlConnection CreatePostgresConnection(this ConnectionRequest request) =>
        new(request.BuildPostgresConnectionString());
}
