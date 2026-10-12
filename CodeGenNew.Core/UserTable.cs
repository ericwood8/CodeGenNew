namespace CodeGenNew.Core;

/// <summary> The user table the sign-in reads, resolved against the schema: the name, password hash, owner and role columns. </summary>
public sealed record UserTablePlan(TableModel Table, ColumnModel UserName, ColumnModel PasswordHash, ColumnModel? Owner, ColumnModel? Role, string RoleWhenTrue, string RoleWhenFalse, ColumnModel? Admin)
{
    /// <summary> True when the role column is a yes/no column (a manager flag), so the role is one of two fixed names; otherwise the column holds the role's name. </summary>
    public bool RoleIsFlag => Role is { IsBooleanColumn: true };
}

/// <summary>
/// People who sign in kept in one of the project's own tables (<c>UserTable=Employee</c>, with <c>UserNameColumn</c>, <c>PasswordHashColumn</c>, <c>UserOwnerColumn</c> and <c>UserRoleColumn</c>).
/// The Jwt group's <c>IUserDirectory</c> is then <c>TableUserDirectory</c> (API_UserDirectory) instead of the single bootstrap administrator. Every template that needs the table asks this one rule.
/// </summary>
public static class UserTable
{
    public const string DefaultRoleWhenTrue = "Manager";
    public const string DefaultRoleWhenFalse = "Employee";

    /// <summary> The shortest value that holds what <c>PasswordService.Hash</c> writes (<c>pbkdf2-sha256$600000$</c> + 24 + <c>$</c> + 44 characters is 90). </summary>
    public const int MinimumHashLength = 90;

    /// <summary> The names a column is looked for under when its setting is left out: the same thing under the names databases give it. Compared without case; the first column that matches is used,
    /// and two matches are an error (name the column in the setting). </summary>
    public static readonly IReadOnlyList<string> UserNameNames = ["Login", "LoginName", "UserName", "User_Name", "UserLogin", "Logon", "LogonName"];
    public static readonly IReadOnlyList<string> PasswordHashNames = ["PasswordHash", "Password_Hash", "PwdHash", "Pword"];
    public static readonly IReadOnlyList<string> AdminNames = ["IsAdministrator", "IsAdmin", "Administrator", "Admin"];

    /// <summary> The project names a user table. </summary>
    public static bool IsConfigured(ProjectSettings project) => project.UserTable is not null;

    /// <summary> The plan, or the reason there is none. The reason is a sentence for the person who wrote the settings. </summary>
    public static (UserTablePlan? Plan, string? Problem) Resolve(DatabaseModel database, ProjectSettings project)
    {
        if (project.UserTable is not { } tableName)
            return (null, null);
        if (!project.ApiAuth)
            return (null, "UserTable needs ApiAuth=true: the user table is where the sign-in looks people up.");

        var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(tableName));
        if (table is null)
            return (null, $"UserTable names {tableName}, which is not a table of the database.");

        if (FindOrInfer(table, "UserNameColumn", project.UserNameColumn, UserNameNames, out var name) is { } nameProblem)
            return (null, nameProblem);
        if (!name!.IsStringColumn)
            return (null, $"UserNameColumn {table.TableName}.{name.Name} must be a text column.");

        if (FindOrInfer(table, "PasswordHashColumn", project.PasswordHashColumn, PasswordHashNames, out var hash) is { } hashProblem)
            return (null, hashProblem);
        if (!hash!.IsStringColumn)
            return (null, $"PasswordHashColumn {table.TableName}.{hash.Name} must be a text column.");
        if (hash.CharacterLength is > 0 and < MinimumHashLength)
            return (null, $"PasswordHashColumn {table.TableName}.{hash.Name} holds {hash.CharacterLength} characters; a password hash needs at least {MinimumHashLength} (varchar(200) is plenty).");

        ColumnModel? owner = null;
        if (project.UserOwnerColumn is { } ownerName)
        {
            if (Find(table, "UserOwnerColumn", ownerName, out owner) is { } ownerProblem)
                return (null, ownerProblem);
            if (!owner!.IsInt32Column)
                return (null, $"UserOwnerColumn {table.TableName}.{owner.Name} must be a whole-number (int) column: it is the owner id in the token.");
        }

        ColumnModel? role = null;
        if (project.UserRoleColumn is { } roleName)
        {
            if (Find(table, "UserRoleColumn", roleName, out role) is { } roleProblem)
                return (null, roleProblem);
            if (!role!.IsBooleanColumn && !role.IsStringColumn)
                return (null, $"UserRoleColumn {table.TableName}.{role.Name} must be a yes/no column (a manager flag) or a text column holding the role's name.");
        }

        // an administrator flag is optional: left out, a yes/no column under one of the usual names is used, and none at all means nobody is an administrator
        ColumnModel? admin = null;
        if (project.UserAdminColumn is { } adminName)
        {
            if (Find(table, "UserAdminColumn", adminName, out admin) is { } adminProblem)
                return (null, adminProblem);
            if (!admin!.IsBooleanColumn)
                return (null, $"UserAdminColumn {table.TableName}.{admin.Name} must be a yes/no column: true makes the person an administrator (the AuthAdminRole).");
        }
        else
        {
            var found = Matches(table, AdminNames).Where(c => c.IsBooleanColumn).ToList();
            if (found.Count > 1)
                return (null, $"{table.TableName} has more than one administrator flag ({string.Join(", ", found.Select(c => c.Name))}); name the one that counts in UserAdminColumn.");
            admin = found.FirstOrDefault();
        }

        return (new UserTablePlan(table, name, hash, owner, role, project.UserRoleWhenTrue, project.UserRoleWhenFalse, admin), null);
    }

    private static IEnumerable<ColumnModel> Matches(TableModel table, IReadOnlyList<string> names) =>
        table.Columns.Where(c => names.Any(n => c.Name.EqualsIgnoreCase(n)));

    /// <summary> The column the setting names; when the setting is left out, the one column that goes by one of the usual <paramref name="names"/>. </summary>
    private static string? FindOrInfer(TableModel table, string setting, string? columnName, IReadOnlyList<string> names, out ColumnModel? column)
    {
        if (columnName is not null)
            return Find(table, setting, columnName, out column);

        var found = Matches(table, names).ToList();
        column = found.Count == 1 ? found[0] : null;
        return found.Count switch
        {
            1 => null,
            0 => $"{setting} is needed with UserTable: {table.TableName} has no column called {string.Join(", ", names.Take(3))} or the like.",
            _ => $"{table.TableName} has more than one column that could be {setting} ({string.Join(", ", found.Select(c => c.Name))}); name the one in {setting}."
        };
    }

    private static string? Find(TableModel table, string setting, string? columnName, out ColumnModel? column)
    {
        column = null;
        if (columnName is null)
            return $"{setting} is needed with UserTable.";
        column = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(columnName));
        return column is null ? $"{setting} names {columnName}, which is not a column of {table.TableName}." : null;
    }
}
