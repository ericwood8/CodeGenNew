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

    /// <summary> The first word of the command line when it is not a flag: <c>generate</c> (every file of a project) or <c>essentials</c> (the files no table drives); null for the one-template form. </summary>
    public string? Command { get; set; }
    /// <summary> --stack: the stacks to generate (Api, WinUI3, React, Angular, Blazor, Rust); empty = the project's Stacks setting. </summary>
    public List<string> Stacks { get; } = [];
    /// <summary> --essentials: a whole-project generate also writes the essentials groups. </summary>
    public bool Essentials { get; set; }
    /// <summary> --groups: the essentials groups to write (App,MainWindow ...); empty = those ticked by default. </summary>
    public List<string> Groups { get; } = [];
    /// <summary> --replace: essentials files that exist are replaced (the default only creates the missing ones). </summary>
    public bool Replace { get; set; }
    /// <summary> --only: run only these templates of the plan. </summary>
    public List<string> Only { get; } = [];
    /// <summary> --dry-run: report what would be written, write nothing. </summary>
    public bool DryRun { get; set; }
    /// <summary> --list: print the stacks and their essentials groups. </summary>
    public bool List { get; set; }
    /// <summary> --delete-stale: delete the files an earlier run wrote that the plan no longer produces (never one that was edited since). </summary>
    public bool DeleteStale { get; set; }
    /// <summary> --build / --test: build and test the stacks after generating. </summary>
    public bool Build { get; set; }
    public bool Test { get; set; }
    /// <summary> --diff: print what differs in every file that exists with other content. </summary>
    public bool Diff { get; set; }
    /// <summary> --projects-dir: the folder of project files; null uses Settings.json's ProjectsDirectory next to the exe. </summary>
    public string? ProjectsDirectory { get; set; }
    /// <summary> Individual project-setting flags (--view-ns and so on), keyed by ProjectSettings key; they win over the project file. </summary>
    public Dictionary<string, string> ProjectOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
}
