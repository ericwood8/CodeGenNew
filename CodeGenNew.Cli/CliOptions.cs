using CodeGenNew.Connections;

namespace CodeGenNew.Cli;

public class CliOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
    public required string Server { get; set; }
    public required string Database { get; set; }
    public string Schema { get; set; } = "dbo";
    public required string Table { get; set; }
    public required string Template { get; set; }
    public string? OutputDirectory { get; set; }
    public bool Trusted { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
}
