namespace CodeGenNew.Connections;

/// <summary> Password (SQL Login only) is held only in memory for the lifetime of one connection attempt -- never persisted. </summary>
public class ConnectionRequest
{
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.SqlServer;
    public required string ServerName { get; init; }
    public required string DatabaseName { get; init; }
    public AuthMode AuthMode { get; init; } = AuthMode.WindowsAuth;
    public string? UserName { get; init; }
    public string? Password { get; init; }

    /// <summary> Defaults to true since dev-box SQL Server instances typically use a self-signed certificate. </summary>
    public bool TrustServerCertificate { get; init; } = true;

    /// <summary> How the connection was made, for the tree's header: "SQL Server, Windows Auth" or "PostgreSQL, SQL Login (ClaudeCode)". A SQLite file has no login. </summary>
    public string DescribeConnection()
    {
        string provider = Provider switch
        {
            DatabaseProvider.SqlServer => "SQL Server",
            DatabaseProvider.PostgreSql => "PostgreSQL",
            DatabaseProvider.MySql => "MySQL",
            _ => "SQLite"
        };
        if (Provider == DatabaseProvider.Sqlite)
            return provider;
        string login = AuthMode == AuthMode.WindowsAuth ? "Windows Auth" : string.IsNullOrWhiteSpace(UserName) ? "SQL Login" : $"SQL Login ({UserName})";
        return $"{provider}, {login}";
    }
}
