using CodeGenNew.Connections;

namespace CodeGenNew.Cli;

public class CliOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
    public required string Server { get; set; }
    public required string Database { get; set; }
    public string Schema { get; set; } = "dbo";
    /// <summary> Null for a database-level template (TemplateConfig.DatabaseOnly), which needs no table. </summary>
    public string? Table { get; set; }
    public required string Template { get; set; }
    public string? OutputDirectory { get; set; }
    public bool Trusted { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    /// <summary> --project: the name of a Projects\<name>.config file; null generates with each template's built-in values. </summary>
    public string? Project { get; set; }
    /// <summary> Individual project-setting flags (--view-ns and so on), keyed by ProjectSettings key; they win over the project file. </summary>
    public Dictionary<string, string> ProjectOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
}
