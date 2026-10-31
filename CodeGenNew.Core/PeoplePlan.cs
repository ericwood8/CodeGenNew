namespace CodeGenNew.Core;

/// <summary> The sentences of the people routes (the first person, a new password, the members of a tenant), the same in the ASP.NET, Python and Rust APIs. </summary>
public static class PeopleWords
{
    public const string FirstPersonInvalidTitle = "The first person needs a user name and a password";
    public const string FirstPersonTakenTitle = "Someone has already been added";
    public const string FirstPersonTakenDetail = "Sign in with that person.";
    public const string PasswordInvalidTitle = "The password needs a user name and a longer password";
    public const string PasswordNotYoursTitle = "Not your password";
    public const string PasswordNotYoursDetail = "Only an administrator sets another person's password.";
    public const string MembersAdminsOnlyTitle = "Administrators only";
    public const string MembersAdminsOnlyDetail = "Only an administrator manages who works in the tenant.";
    public const string MemberExistsTitle = "Already a member";
    public const string MemberExistsDetail = "That person already works in this tenant.";
    public const string MemberSelfTitle = "You cannot remove yourself";
    public const string MemberSelfDetail = "You cannot remove yourself from the tenant.";

    public const string FirstPersonPath = "/api/auth/first-person";
    public const string PasswordPath = "/api/auth/password";
    public const string MembersPath = "/api/auth/members";
    public const string CandidatesPath = "/api/auth/members/candidates";
}

/// <summary> What the people routes of an API are made of: the user table, the first person, and with a tenant table and a membership table the members of a tenant. One rule for the ASP.NET, Python and Rust
/// APIs, so the routes and the screens that call them cannot drift apart. </summary>
public sealed record PeoplePlan(UserTablePlan User, ColumnModel Key, ColumnModel? NameColumn, FirstPersonPlan? First, TenantMembership? Membership, TableModel? TenantTable, ColumnModel? TenantKey)
{
    /// <summary> The columns of the user table that name a person for a list, in the order they are looked for. </summary>
    public static readonly string[] NameColumns = ["Name", "FullName", "DisplayName", "PersonName", "EmployeeName", "Title"];

    /// <summary> The plan, or null when the project has no sign-in with a user table. </summary>
    public static PeoplePlan? For(DatabaseModel database, ProjectSettings project)
    {
        if (!project.ApiAuth || UserTable.Resolve(database, project).Plan is not { } user || user.Table.PrimaryKeyColumns is not [var key])
            return null;
        var nameColumn = user.Table.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && !c.IsCredentialColumn && !c.Name.EqualsIgnoreCase(user.UserName.Name) && NameColumns.Any(n => n.EqualsIgnoreCase(c.Name)));

        var tenantTable = project.TenantTable is not null ? TenantPlan.TenantTableOf(database, project) : null;
        var tenantKey = tenantTable is null ? null : TenantPlan.KeyOf(tenantTable);
        var membership = tenantKey is null || !key.IsInt32Column ? null : TenantPlan.MembershipOf(database, project);
        bool ownTenant = tenantKey is not null && membership is null && TenantPlan.OwnedBy(user.Table, project) is not null;
        var first = ownTenant ? null : FirstPersonPlan.For(user, project).Plan;
        return new PeoplePlan(user, key, nameColumn, first, membership, membership is null ? null : tenantTable, membership is null ? null : tenantKey);
    }

    /// <summary> Whether the members of a tenant are managed over the API: a membership table says who works in which tenant. </summary>
    public bool HasMembers => Membership is not null && TenantTable is not null && TenantKey is not null;

    /// <summary> Every column of the user table the people routes read or write. </summary>
    public IEnumerable<ColumnModel> UserColumns =>
        new[] { Key, User.UserName, User.PasswordHash, NameColumn }.OfType<ColumnModel>()
            .Concat(First?.Values.Select(v => v.Column) ?? []).DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase);
}
