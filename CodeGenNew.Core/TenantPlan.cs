namespace CodeGenNew.Core;

/// <summary> The sentences and problem types of the tenant routes, the same in every stack that writes them. </summary>
public static class TenantWords
{
    public const string NoTenantType = ProblemTypes.Prefix + "tenant-required";
    public const string NotMemberType = ProblemTypes.Prefix + "tenant-not-member";
    public const string OnboardingType = ProblemTypes.Prefix + "tenant-onboarding";
    public const string InactiveType = ProblemTypes.Prefix + "tenant-inactive";

    public const string NoTenant = "The signed-in person is not working in a tenant. Sign in to a tenant, or switch to one.";
    public const string NotMember = "The signed-in person is not a member of that tenant.";
    public const string CreateByOnboarding = "Tenants are created by POST /api/tenants/onboard, not by an ordinary create.";
    public const string NeverDeleted = "A tenant is switched off (its active flag), never deleted.";
    public const string Inactive = "The tenant of this token is switched off. Sign in again.";
    public const string HostType = ProblemTypes.Prefix + "tenant-host";
    public const string Host = "This address belongs to another tenant than the one you are signed in to.";
    public const string SignInLabel = "Tenant";
    public const string SignInHint = "Leave blank to work in your first tenant.";
    /// <summary> The route that lists the tenants a person can sign in to, for the drop-down of the sign-in screen (no sign-in needed). </summary>
    public const string SignInListPath = "/api/auth/sign-in-tenants";
    public const string SignInRefused = "You are not a member of that tenant, or it is switched off.";
    public const string OnlyPlatform = "Only a platform administrator (an administrator who is not working in a tenant) onboards tenants.";
}

/// <summary> The membership table: which people belong to which tenant, optionally with a role per tenant. </summary>
public sealed record TenantMembership(TableModel Table, ColumnModel User, ColumnModel Tenant, ColumnModel? Role);

/// <summary> The user table whose people belong to tenants by the membership table; <c>Key</c> is the person's whole-number key. </summary>
public sealed record TenantPeople(UserTablePlan User, TenantMembership Membership, ColumnModel Key);

/// <summary> A table whose rows belong to one tenant: <c>Column</c> is its NOT NULL whole-number tenant column. </summary>
public sealed record TenantOwned(TableModel Table, ColumnModel Column);

/// <summary> Shared-schema multi-tenancy (<c>TenantTable=Tenant</c>): every table with a NOT NULL whole-number <c>TenantColumn</c> (default <c>TenantId</c>) that is a foreign key to the tenant table is tenant-owned, and the caller's tenant (a claim in the
/// token) decides which rows they reach. The context filters every read, the API fills the tenant column on create and writes the caller's tenant on update (a row never moves to another tenant), and the tenant table itself shows only the caller's own tenant.
/// <c>NoTenantTables</c> leaves a table out; <c>TenantMembership=UserTenant:UserId:TenantId:Role</c> names the junction that lets one person work in several tenants. </summary>
public static class TenantPlan
{
    /// <summary> The place in a dashboard statement (<see cref="DashboardPlan.BuildFor"/>) where the API puts the caller's tenant key, or NULL for a caller in no tenant. </summary>
    public const string DashboardToken = "{tenant}";

    public const string DefaultColumn = "TenantId";

    public static bool IsEnabled(ProjectSettings project) => project.TenantTable is not null;

    /// <summary> The tenant table, or null (no setting, or not a table of the database). </summary>
    public static TableModel? TenantTableOf(DatabaseModel database, ProjectSettings project) =>
        project.TenantTable is { } name ? database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name)) : null;

    /// <summary> Whether a web front end shows the tenant name and a switcher: it signs in, and the API serves <c>GET /api/auth/tenants</c> (a membership table is named). </summary>
    public static bool HasSwitcher(ProjectSettings project) => project.Auth && project.ApiAuth && project.TenantTable is not null && project.TenantMembership is not null;

    /// <summary> Whether the sign-in page of a web front end has a box for the tenant to sign in to: the login takes a <c>tenantId</c> from a person who belongs to several tenants (a membership table is named). </summary>
    public static bool HasSignInBox(ProjectSettings project) => HasSwitcher(project);

    /// <summary> The people of the user table who belong to a tenant by a membership row: <c>TenantMembership</c> with a <c>UserTable</c>. A tenant then shows only its own members; a platform administrator shows all. Null when the project
    /// has no such pair, or the membership row could not be made for a person added inside a tenant (a column other than the key, the person and the tenant is required), or the people are tenant-owned by their own row already. </summary>
    public static TenantPeople? PeopleOf(DatabaseModel database, ProjectSettings project)
    {
        if (!project.ApiAuth || TenantTableOf(database, project) is null || MembershipOf(database, project) is not { } membership)
            return null;
        if (UserTable.Resolve(database, project).Plan is not { } plan || plan.Table == membership.Table || OwnedBy(plan.Table, project) is not null || plan.Table.PrimaryKeyColumns is not [{ IsInt32Column: true } key])
            return null;
        bool otherwiseRequired = membership.Table.Columns.Any(c => !c.IsPrimaryKey && !c.IsIdentity && c != membership.User && c != membership.Tenant && !c.IsNullable);
        return otherwiseRequired ? null : new TenantPeople(plan, membership, key);
    }

    /// <summary> Whether the onboarding body can carry the first administrator as a new person (<c>admin</c> and <c>adminPassword</c>): there is a user table, and a person can be tied to the new tenant, by a membership
    /// row or by the tenant column of their own row. </summary>
    public static bool OnboardsNewAdmin(DatabaseModel database, ProjectSettings project) =>
        UserTable.Resolve(database, project).Plan is { } plan && (MembershipOf(database, project) is not null || OwnedBy(plan.Table, project) is not null);

    /// <summary> Whether the WinUI3 app signs in: <c>Auth</c> with the WinUI3 stack and a user table of the sign-in (<c>ApiAuth</c>). The app reads the database itself, so it checks the password against the user table
    /// and gives the context its tenant scope. </summary>
    public static bool HasDesktopSignIn(ProjectSettings project) =>
        project.Auth && project.ApiAuth && project.UserTable is not null && project.Stacks.Any(s => s.EqualsIgnoreCase("WinUI3"));

    /// <summary> Whether the WinUI3 screens apply the read and write roles: the app signs in, so the role of the person is known. Only the screens ask; the app reads the database with its own login. </summary>
    public static bool HasDesktopRoles(ProjectSettings project) =>
        HasDesktopSignIn(project) && (project.ReadRoles.Length > 0 || project.WriteRoles.Length > 0 || project.TableReadRoles is not null || project.TableWriteRoles is not null);

    /// <summary> Whether the WinUI3 sign-in works in a tenant: the sign-in takes the tenant to work in and the window shows it. </summary>
    public static bool HasDesktopTenants(ProjectSettings project) => HasDesktopSignIn(project) && project.TenantTable is not null;

    /// <summary> Whether the WinUI3 window offers a switcher: the person can belong to several tenants (a membership table is named). </summary>
    public static bool HasDesktopSwitcher(ProjectSettings project) => HasDesktopTenants(project) && project.TenantMembership is not null;

    /// <summary> Whether the WinUI3 app signs in, so the person is known to the guards and approvals (<c>DesktopCaller.Name</c>). </summary>
    public static bool DesktopSetsCaller(ProjectSettings project) =>
        DesktopApprovalsCode.GatesWriteCaller(project) || (project.ApiAuth && project.RowApprovals.Length > 0);

    /// <summary> Whether the Python and Rust APIs write their tenant code: <c>TenantTable</c> with sign-in. The EF-only rule of the ASP.NET API does not apply: their routers filter by hand. </summary>
    public static bool HasApiTenancy(ProjectSettings project) => project.ApiAuth && project.TenantTable is not null;

    /// <summary> A tenant-owned table has no CSV export or import in the Python and Rust APIs: their CSV routes read every row, and a caller must not reach another tenant's. </summary>
    public static bool NoApiCsv(TableModel table, ProjectSettings project) => HasApiTenancy(project) && OwnedBy(table, project) is not null;

    public static bool IsTenantTable(TableModel table, ProjectSettings project) => project.TenantTable is { } name && table.TableName.EqualsIgnoreCase(name);

    /// <summary> The C# that names a tenant for a person, over a row <c>t</c> of the tenant table: its Name or Title column, else the key as text. </summary>
    public static string TextExpression(TableModel tenantTable, ColumnModel tenantKey)
    {
        var text = tenantTable.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && (c.Name.EqualsIgnoreCase("Name") || c.Name.EqualsIgnoreCase("Title")));
        return text is null ? $"t.{tenantKey.CSharpName()}.ToString()" : text.IsNullable ? $"t.{text.CSharpName()} ?? \"\"" : $"t.{text.CSharpName()}";
    }

    /// <summary> The yes / no column that marks a tenant as deleted, or null. The tenant table cannot be listed in <c>SoftDeleteTables</c> (it takes no delete), so the flag is found by its convention: the schema's soft-delete pair, else a NOT NULL yes / no column named IsDeleted. The sign-in list leaves such a tenant out. </summary>
    public static ColumnModel? DeletedFlagOf(TableModel tenantTable) =>
        tenantTable.IsDeletedColumn is { IsBooleanColumn: true, IsNullable: false } pair ? pair
        : tenantTable.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(SoftDeletePlan.DefaultFlag) && c.IsBooleanColumn && !c.IsNullable);

    public static ColumnModel? KeyOf(TableModel tenantTable) => tenantTable.PrimaryKeyColumns is [{ IsInt32Column: true } key] ? key : null;

    /// <summary> The tenant column of a tenant-owned table, or null. </summary>
    public static TenantOwned? OwnedBy(TableModel table, DatabaseModel database, ProjectSettings project) =>
        TenantTableOf(database, project) is null ? null : OwnedBy(table, project);

    /// <summary> The same without the database: the table has a NOT NULL int column named TenantColumn that is a foreign key to the tenant table. </summary>
    public static TenantOwned? OwnedBy(TableModel table, ProjectSettings project)
    {
        if (!project.ApiAuth || project.TenantTable is not { } tenantName || table.TableName.EqualsIgnoreCase(tenantName) || project.NoTenant(table.TableName))
            return null;
        var column = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(project.TenantColumn));
        if (column is not { IsInt32Column: true, IsNullable: false })
            return null;
        bool points = table.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(column.Name) && f.ReferencedTable.EqualsIgnoreCase(tenantName));
        return points ? new TenantOwned(table, column) : null;
    }

    /// <summary> The tenant column of an editable child table (<c>EditableChildren</c>), or null when the child is not tenant-owned. The parent's save stamps it on every child row, the way an ordinary create does. </summary>
    public static ColumnModel? ChildTenantColumn(ChildForeignKeyModel child, ProjectSettings project)
    {
        if (!project.ApiAuth || project.TenantTable is not { } tenantName || child.ReferencingTable.EqualsIgnoreCase(tenantName) || project.NoTenant(child.ReferencingTable))
            return null;
        var column = child.ReferencingTableColumns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(project.TenantColumn));
        if (column is not { IsInt32Column: true, IsNullable: false })
            return null;
        bool points = child.ReferencingTableForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(column.Name) && f.ReferencedTable.EqualsIgnoreCase(tenantName));
        return points ? column : null;
    }

    public static IReadOnlyList<TenantOwned> OwnedTables(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => OwnedBy(t, database, project)).OfType<TenantOwned>().OrderBy(o => o.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The text column of the tenant table that names the tenant of a host name (<c>TenantHostColumn</c>), or null when the project names none or the column cannot be used (see <see cref="Problems"/>). </summary>
    public static ColumnModel? HostColumnOf(DatabaseModel database, ProjectSettings project) =>
        project.TenantHostColumn is { } name ? TenantTableOf(database, project)?.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name) && c.IsStringColumn && !c.IsLongTextColumn) : null;

    /// <summary> The membership junction the project names, or null when it names none or cannot be used (see <see cref="Problems"/>). </summary>
    public static TenantMembership? MembershipOf(DatabaseModel database, ProjectSettings project)
    {
        if (project.TenantMembership is not { } setting)
            return null;
        string[] parts = setting.Split(':', StringSplitOptions.TrimEntries);
        var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(parts[0]));
        if (table is null || parts.Length < 3)
            return null;
        ColumnModel? Named(string name) => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var user = Named(parts[1]);
        var tenant = Named(parts[2]);
        var role = parts.Length > 3 && parts[3].Length > 0 ? Named(parts[3]) : null;
        if (user is not { IsInt32Column: true } || tenant is not { IsInt32Column: true } || (parts.Length > 3 && parts[3].Length > 0 && role is not { IsStringColumn: true }))
            return null;
        return new TenantMembership(table, user, tenant, role);
    }

    /// <summary> Every reason the tenant settings cannot be served, empty when they can (or are not set). </summary>
    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        if (!IsEnabled(project))
        {
            if (project.TenantMembership is not null || project.NoTenantTables.Length > 0 || project.TenantHostColumn is not null)
                problems.Add("TenantMembership, NoTenantTables and TenantHostColumn need TenantTable.");
            return problems;
        }
        if (!project.ApiAuth)
            problems.Add("TenantTable needs ApiAuth=true (the caller's tenant is a claim of the token).");
        if (project.AccessModeFor(database.Dialect) != AccessMode.Ef)
            problems.Add("TenantTable needs AccessMode=Ef (the tenant is a query filter of the EF context).");
        string prefix = $"TenantTable: {project.TenantTable}";
        var tenantTable = TenantTableOf(database, project);
        if (tenantTable is null)
        {
            problems.Add($"{prefix} is not a table of the database.");
            return problems;
        }
        if (KeyOf(tenantTable) is null)
            problems.Add($"{prefix} needs a single whole-number key.");
        if (OwnedTables(database, project).Count == 0 && project.ApiAuth)
            problems.Add($"{prefix}: no other table has a NOT NULL whole-number column {project.TenantColumn} that is a foreign key to {tenantTable.TableName} (TenantColumn names another column).");
        foreach (var table in database.Tables.Where(t => t.HasPrimaryKey && !IsTenantTable(t, project) && !project.NoTenant(t.TableName) && !project.NoApiFor(t.TableName, t.LookupShape)))
        {
            if (table.Columns.Any(c => c.Name.EqualsIgnoreCase(project.TenantColumn)) && OwnedBy(table, project) is null)
                problems.Add($"{prefix}: {table.TableName} has a column {project.TenantColumn} but not as a NOT NULL whole-number foreign key to {tenantTable.TableName}, so its rows would be shown to every tenant. Add the foreign key (the history table of an audit trigger copies the column without it), leave the table out with NoApiTables, or list it in NoTenantTables when every tenant may see it.");
        }
        foreach (string name in project.NoTenantTables.Where(n => !database.Tables.Any(t => t.TableName.EqualsIgnoreCase(n))))
            problems.Add($"NoTenantTables: {name} is not a table of the database.");
        if (project.TenantHostColumn is { } hostColumn && HostColumnOf(database, project) is null)
            problems.Add($"TenantHostColumn: {hostColumn} must be a text column of {tenantTable.TableName} (its value is the first label of the host name: acme.example.com is the tenant whose {hostColumn} is acme).");
        if (project.TenantMembership is { } membership)
        {
            if (MembershipOf(database, project) is null)
                problems.Add($"TenantMembership: {membership} must name a table, its whole-number person column, its whole-number tenant column and optionally a text role column (TenantMembership=UserTenant:UserId:TenantId:Role).");
            else if (project.UserTable is null)
                problems.Add("TenantMembership needs UserTable (the people who sign in are rows of a table, and a membership names one of them).");
        }
        return problems;
    }
}
