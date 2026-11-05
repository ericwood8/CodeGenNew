namespace CodeGenNew.Core;

/// <summary> The project's <c>Permissions</c>: a name for something a role may do, held by one or more roles. A role list that names a permission (<c>@edit_billing</c>) means the roles that hold it, so one
/// sentence in the settings changes every route and screen that checks the list. The roles are fixed when the code is generated; roles and permissions kept in tables are not covered. </summary>
public static class PermissionPlan
{
    public const string Marker = "@";

    /// <summary> The roles that hold <paramref name="permission"/>; empty for a permission the settings do not list. </summary>
    public static IReadOnlyList<string> RolesOf(ProjectSettings project, string permission)
    {
        foreach (string entry in (project.Permissions ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = entry.IndexOf(':');
            if (colon > 0 && entry[..colon].Trim().EqualsIgnoreCase(permission))
                return entry[(colon + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
        return [];
    }

    /// <summary> <paramref name="roles"/> with every <c>@permission</c> replaced by the roles that hold it in the settings. With <c>PermissionTables</c> the entry stays too, because a person can hold the permission through a role kept in the database. </summary>
    public static IReadOnlyList<string> Expand(ProjectSettings project, IEnumerable<string> roles) =>
        roles.SelectMany(r => r.StartsWith(Marker) ? RolesOf(project, r[Marker.Length..].Trim()).Concat(PermissionTables.IsConfigured(project) ? [Marker + r[Marker.Length..].Trim()] : []) : [r]).ToList();
}
