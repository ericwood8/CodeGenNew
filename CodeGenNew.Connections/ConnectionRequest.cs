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
}
