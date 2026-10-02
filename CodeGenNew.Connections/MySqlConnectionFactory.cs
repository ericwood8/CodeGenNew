using MySqlConnector;

namespace CodeGenNew.Connections;

public static class MySqlConnectionFactory
{
    /// <summary> ServerName is "host" or "host:port" (the default port is 3306). MySQL has no Windows-authentication mode here, so a user name (and
    /// normally a password) is always required. The database name is also the schema name: a MySQL "database" and "schema" are the same thing. </summary>
    public static string BuildMySqlConnectionString(this ConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            throw new ArgumentException("UserName is required for MySQL.", nameof(request));

        string host = request.ServerName;
        uint port = 3306;
        int colon = host.LastIndexOf(':');
        if (colon > 0 && uint.TryParse(host[(colon + 1)..], out uint parsedPort))
        {
            port = parsedPort;
            host = host[..colon];
        }

        return new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            Database = request.DatabaseName,
            UserID = request.UserName,
            Password = request.Password ?? "",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 30,
            CharacterSet = "utf8mb4"
        }.ConnectionString;
    }

    public static MySqlConnection CreateMySqlConnection(this ConnectionRequest request) =>
        new(request.BuildMySqlConnectionString());
}
