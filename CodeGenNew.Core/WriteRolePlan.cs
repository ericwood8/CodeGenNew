namespace CodeGenNew.Core;

/// <summary> Who may read and who may create, change and delete the rows of a table (the project's <c>ReadRoles</c>, <c>TableReadRoles</c>, <c>WriteRoles</c> and <c>TableWriteRoles</c>, with <c>ApiAuth</c>).
/// The administrator role (<c>AuthAdminRole</c>) always may use a table that has a rule. The REST routes, the OData routes and the Angular screens all ask this one rule. </summary>
public static class WriteRolePlan
{
    /// <summary> The roles allowed to write the table, the administrator role first; empty when any signed-in person may (no rule, or no <c>ApiAuth</c>). </summary>
    public static IReadOnlyList<string> For(ProjectSettings project, string table) => Resolve(project, table, project.WriteRoles, project.TableWriteRoles);

    public static IReadOnlyList<string> For(ProjectSettings project, TableModel table) => For(project, table.TableName);

    /// <summary> The roles allowed to read the table, the administrator role first; empty when any signed-in person may. </summary>
    public static IReadOnlyList<string> ReadFor(ProjectSettings project, string table) => Resolve(project, table, project.ReadRoles, project.TableReadRoles);

    public static IReadOnlyList<string> ReadFor(ProjectSettings project, TableModel table) => ReadFor(project, table.TableName);

    /// <summary> The roles as the items of a C# or TypeScript array: <c>"Administrator", "Manager"</c>, or with single quotes for TypeScript. </summary>
    public static string Literal(IReadOnlyList<string> roles, char quote = '"') =>
        string.Join(", ", roles.Select(r => quote + r.Replace("\\", "\\\\").Replace(quote.ToString(), "\\" + quote) + quote));

    private static IReadOnlyList<string> Resolve(ProjectSettings project, string table, string[] wholeProject, string? perTable)
    {
        if (!project.ApiAuth)
            return [];
        IReadOnlyList<string> roles = TableEntry(perTable, table) ?? (IReadOnlyList<string>)wholeProject;
        if (roles.Count == 0)
            return [];
        return new[] { project.AuthAdminRole }.Concat(roles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string>? TableEntry(string? setting, string table)
    {
        foreach (string entry in (setting ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = entry.IndexOf(':');
            if (colon < 0 || !entry[..colon].Trim().EqualsIgnoreCase(table))
                continue;
            return entry[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        return null;
    }
}
