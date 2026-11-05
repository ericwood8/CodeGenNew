namespace CodeGenNew.Core;

/// <summary> The tables a person's roles and permissions are kept in: the roles, the permissions, which role holds which permission, and which person has which role. Each junction column is a foreign key to its table. </summary>
public sealed record PermissionTablesPlan(
    TableModel Role, ColumnModel RoleKey, ColumnModel RoleName,
    TableModel Permission, ColumnModel PermissionKey, ColumnModel PermissionName,
    TableModel RolePermission, ColumnModel RolePermissionRole, ColumnModel RolePermissionPermission,
    TableModel UserRole, ColumnModel UserRoleUser, ColumnModel UserRoleRole,
    UserTableShape User, ColumnModel UserKey,
    ColumnModel? UserRoleTenant = null, ColumnModel? RoleTenant = null)
{
    /// <summary> Whether the roles depend on the tenant the person works in: a role is held in one tenant (a tenant column on the person's role rows), or a tenant keeps its own roles (a tenant column on the role table). </summary>
    public bool PerTenant => UserRoleTenant is not null || RoleTenant is not null;
}

/// <summary> <c>PermissionTables=Role,Permission,RolePermission,UserRole</c>: roles and permissions kept in tables instead of fixed in the settings. At sign-in the API adds a role claim for each role of the person and a
/// permission claim for each permission those roles hold; a role list that names <c>@edit_billing</c> is then allowed for a person whose token carries that permission.
/// With <c>TenantTable</c>, a UserRole row that has the tenant column holds the role only in that tenant, and a Role row that has it belongs to that tenant alone; the claims are those of the tenant in the token. A nullable tenant column also lets a row with NULL serve every tenant (a role all tenants share). </summary>
public static class PermissionTables
{
    public const string PermissionClaim = "permission";

    public static bool IsConfigured(ProjectSettings project) => project.ApiAuth && project.PermissionTables.Length > 0;

    public static (PermissionTablesPlan? Plan, string? Problem) Resolve(DatabaseModel database, ProjectSettings project)
    {
        if (project.PermissionTables.Length == 0)
            return (null, null);
        if (!project.ApiAuth)
            return (null, "PermissionTables needs ApiAuth=true: the permissions go into the token at sign-in.");
        if (project.PermissionTables.Length != 4)
            return (null, "PermissionTables names four tables: PermissionTables=Role,Permission,RolePermission,UserRole.");
        var (user, userProblem) = UserTable.Resolve(database, project);
        if (user is null)
            return (null, userProblem ?? "PermissionTables needs UserTable: it says who holds each role.");

        var tables = new TableModel[4];
        for (int i = 0; i < 4; i++)
        {
            string name = project.PermissionTables[i];
            tables[i] = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name))!;
            if (tables[i] is null)
                return (null, $"PermissionTables names {name}, which is not a table of the database.");
        }
        var (role, permission, rolePermission, userRole) = (tables[0], tables[1], tables[2], tables[3]);

        if (Key(role) is not { } roleKey) return (null, KeyProblem(role));
        if (Key(permission) is not { } permissionKey) return (null, KeyProblem(permission));
        if (Key(user.Table) is not { } userKey) return (null, KeyProblem(user.Table));
        if (NameOf(role) is not { } roleName) return (null, NameProblem(role));
        if (NameOf(permission) is not { } permissionName) return (null, NameProblem(permission));

        if (LinkTo(rolePermission, role) is not { } rpRole) return (null, LinkProblem(rolePermission, role));
        if (LinkTo(rolePermission, permission) is not { } rpPermission) return (null, LinkProblem(rolePermission, permission));
        if (LinkTo(userRole, user.Table) is not { } urUser) return (null, LinkProblem(userRole, user.Table));
        if (LinkTo(userRole, role) is not { } urRole) return (null, LinkProblem(userRole, role));

        return (new PermissionTablesPlan(role, roleKey, roleName, permission, permissionKey, permissionName, rolePermission, rpRole, rpPermission, userRole, urUser, urRole, user, userKey,
            TenantColumnOf(userRole, project), TenantColumnOf(role, project)), null);
    }

    /// <summary> The whole-number tenant column of a table that points at the tenant table, or null. A NOT NULL one makes every row belong to one tenant; a nullable one lets a NULL row serve every tenant (a role all tenants share). </summary>
    /// <summary> The role table and its nullable tenant column when the table is listed in <c>NoTenantTables</c>: a tenant reads its own roles and the shared ones (NULL), so the role list and the dashboard count both. </summary>
    public static (TableModel Table, ColumnModel Column)? SharedRoleTenant(DatabaseModel database, ProjectSettings project)
    {
        var plan = project.ApiAuth && project.TenantTable is not null ? Resolve(database, project).Plan : null;
        return plan?.RoleTenant is { IsNullable: true } column && project.NoTenant(plan.Role.TableName) ? (plan.Role, column) : null;
    }

    /// <summary> The same, asked of one table without the database: the nullable tenant column when this table is the role table of <c>PermissionTables</c> and is listed in <c>NoTenantTables</c>. </summary>
    public static ColumnModel? SharedRoleTenant(TableModel table, ProjectSettings project) =>
        IsConfigured(project) && project.PermissionTables.Length == 4 && project.PermissionTables[0].EqualsIgnoreCase(table.TableName) && project.NoTenant(table.TableName) && TenantColumnOf(table, project) is { IsNullable: true } column ? column : null;

    private static ColumnModel? TenantColumnOf(TableModel table, ProjectSettings project)
    {
        if (!project.ApiAuth || project.TenantTable is not { } tenantName)
            return null;
        var column = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(project.TenantColumn));
        bool points = column is { IsInt32Column: true } && table.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(column.Name) && f.ReferencedTable.EqualsIgnoreCase(tenantName));
        return points ? column : null;
    }

    private static ColumnModel? Key(TableModel table) =>
        table.PrimaryKeyColumns.Count == 1 && table.PrimaryKeyColumns[0].IsInt32Column ? table.PrimaryKeyColumns[0] : null;

    private static string KeyProblem(TableModel table) => $"PermissionTables: {table.TableName} needs a primary key of one whole-number (int) column.";

    private static ColumnModel? NameOf(TableModel table) => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase("Name") && c.IsStringColumn);

    private static string NameProblem(TableModel table) => $"PermissionTables: {table.TableName} needs a text column called Name.";

    private static ColumnModel? LinkTo(TableModel from, TableModel to)
    {
        var keys = from.ForeignKeys.Where(fk => fk.ReferencedTable.EqualsIgnoreCase(to.TableName) && fk.ReferencingColumns.Count == 1).ToList();
        return keys.Count == 1 ? from.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(keys[0].ReferencingColumns[0])) : null;
    }

    private static string LinkProblem(TableModel from, TableModel to) => $"PermissionTables: {from.TableName} needs exactly one foreign key to {to.TableName} on a single column.";
}
