namespace CodeGenNew.SchemaIntrospection;

/// <summary> Recognizes framework/replication/scaffolding tables by name so they can be excluded from a
/// table list a person is meant to pick a real table from (Docs/specs.md section 9.4). Not exercised by
/// the CLI's single-table generate path, but used by the TreeView (CodeGenNew.App) to exclude
/// system/framework tables from the node list. </summary>
public static class SystemTableFilter
{
    private static readonly string[] ExactNames =
    [
        "schema_info", "scope_config", "scope_parameters", "scope_templates",
        "sysdiagrams", "syspublications", "sysreplservers", "sysschemaarticles",
        "syssubscriptions", "systranschemas", "dtproperties", "__RefactorLog", "__MigrationHistory"
    ];

    private static readonly string[] Prefixes =
    [
        "conflict_", "MSpeer_", "MSpub_", "MSreplication_", "MSsubscription_", "sysarticle", "aspnet_"
    ];

    public static bool IsSystemTable(string tableName) =>
        ExactNames.Any(n => tableName.Equals(n, StringComparison.OrdinalIgnoreCase))
        || Prefixes.Any(p => tableName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        || tableName.EndsWith("_tracking", StringComparison.OrdinalIgnoreCase);
}
