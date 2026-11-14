using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> The shapes that need more than one table to see: a polymorphic link (its tables come from a check constraint), a snapshot (the parents it copies from), an archive beside its table, and the project-wide sets
/// (tenant table, user table, roles and permissions, feature flags). Like <see cref="TableShapeDetector"/> it only suggests. </summary>

public static class DatabaseShapeDetector
{
    private static readonly string[] TypeSuffixes = ["Type", "Table", "Kind"];
    private static readonly string[] TenantNames = ["TenantId", "OrganizationId", "AccountId", "CompanyId", "OrgId"];
    private static readonly string[] ArchiveSuffixes = ["Archive", "_Archive", "History", "_History"];
    private static readonly string[] FeatureDefaultNames = ["DefaultEnabled", "DefaultOn", "IsEnabled", "Enabled"];

    public static IReadOnlyList<DatabaseShapeDetection> Of(DatabaseModel database, ProjectSettings project) => [.. Detect(database, project)];

    private static IEnumerable<DatabaseShapeDetection> Detect(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var s in TableShapeDetector.Of(table, project))
                yield return new(table.TableName, s);
            foreach (var s in new[] { Polymorphic(table, database, project), Snapshot(table, database, project), Archive(table, database, project) })
                if (s is not null)
                    yield return new(table.TableName, s);
        }
        foreach (var s in new[] { Tenant(database, project), User(database, project), Permissions(database, project), Features(database, project) })
            if (s is not null)
                yield return new("", s);
    }

    private static ShapeSuggestion? Polymorphic(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (PolymorphicPlan.IsLinkTable(table.TableName, project) || table.PrimaryKeyColumns is not [{ IsIntegerColumn: true }])
            return null;
        foreach (var type in table.Columns.Where(c => c.IsStringColumn && !c.IsPrimaryKey))
        {
            string? suffix = TypeSuffixes.FirstOrDefault(s => type.Name.EndsWithIgnoreCase(s) && type.Name.Length > s.Length);
            if (suffix is null)
                continue;
            string stem = type.Name[..^suffix.Length];
            var id = table.Columns.FirstOrDefault(c => c.IsIntegerColumn && !c.IsPrimaryKey && c.Name.EqualsIgnoreCase(stem + "Id"));
            if (id is null || table.ForeignKeys.Any(f => f.ReferencingColumns.Any(c => c.EqualsIgnoreCase(id.Name) || c.EqualsIgnoreCase(type.Name))))
                continue;
            var targets = database.Tables.Where(t => !t.TableName.EqualsIgnoreCase(table.TableName)
                && table.CheckConstraints.Any(k => k.Definition.ContainsIgnoreCase(type.Name) && Regex.IsMatch(k.Definition, $"'{Regex.Escape(t.TableName)}'", RegexOptions.IgnoreCase)))
                .Select(t => t.TableName).ToList();
            if (targets.Count < 2)
                return new ShapeSuggestion("Polymorphic link", "PolymorphicLinks", $"{table.TableName}:{type.Name}:{id.Name}", false,
                    $"{type.Name} and {id.Name} look like a link to rows of several tables, but no check constraint names the tables; name them as PolymorphicLinks={table.TableName}:{type.Name}:{id.Name}:Table1|Table2");
            string item = $"{table.TableName}:{type.Name}:{id.Name}:{string.Join("|", targets)}";
            var listed = project.WithOverrides([new("PolymorphicLinks", string.Join(",", project.PolymorphicLinks.Append(item)))]);
            string? problem = PolymorphicPlan.Problems(database, listed).FirstOrDefault(p => p.StartsWith($"PolymorphicLinks: {table.TableName}", StringComparison.Ordinal));
            return new ShapeSuggestion("Polymorphic link", "PolymorphicLinks", item, problem is null, problem is null ? $"{type.Name} names one of {string.Join(", ", targets)} and {id.Name} is its key" : $"{type.Name} and {id.Name} look like a link, but {problem}");
        }
        return null;
    }

    private static ShapeSuggestion? Snapshot(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (SnapshotPlan.IsListed(table.TableName, project) || table.PrimaryKeyColumns.Count != 1 || !table.Columns.Any(c => c.Name.EqualsIgnoreCase("Quantity")))
            return null;
        var copies = new List<string>();
        foreach (var fk in table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && f.ReferencedColumns.Count == 1))
        {
            var parent = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(fk.ReferencedTable));
            if (parent is null || parent.TableName.EqualsIgnoreCase(table.TableName))
                continue;
            foreach (var source in parent.Columns.Where(c => !c.IsPrimaryKey && !c.IsAuditColumn && !c.IsComputed))
                foreach (var own in table.Columns.Where(c => !c.IsPrimaryKey && !c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0]) && !copies.Any(x => x.StartsWith(c.Name + "<-", StringComparison.OrdinalIgnoreCase))))
                {
                    bool named = own.Name.EqualsIgnoreCase(source.Name) || own.Name.EqualsIgnoreCase(parent.TableName + source.Name) || (source.Name.EqualsIgnoreCase("Price") && own.Name.EqualsIgnoreCase("UnitPrice"));
                    if (named && own.HasSameTypeAs(source) && own.CharacterLength == source.CharacterLength)
                        copies.Add($"{own.Name}<-{parent.TableName}.{source.Name}");
                }
        }
        return copies.Count == 0 ? null
            : new ShapeSuggestion("Snapshot at checkout", "SnapshotColumns", $"{table.TableName}:{string.Join("+", copies)}", false,
                $"has {table.Columns.First(c => c.Name.EqualsIgnoreCase("Quantity")).Name} and columns that look like copies of its parents' columns; if they are copies taken when the row is saved, SnapshotColumns={table.TableName}:{string.Join("+", copies)} writes them (check the list, this is a guess)");
    }

    private static ShapeSuggestion? Archive(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (ArchivePlan.Entries(project).Any(e => e.Table.EqualsIgnoreCase(table.TableName)))
            return null;
        var archive = database.Tables.FirstOrDefault(t => ArchiveSuffixes.Any(s => t.TableName.EqualsIgnoreCase(table.TableName + s))
            && table.Columns.Where(c => !c.IsComputed).All(c => t.Columns.Any(a => a.Name.EqualsIgnoreCase(c.Name))));
        return archive is null ? null
            : new ShapeSuggestion("Archive", "ArchiveTables", $"{table.TableName}:90", false,
                $"{archive.TableName} has every column of {table.TableName}; ArchiveTables={table.TableName}:DAYS keeps that many days of rows and moves older ones there (choose the days; the plan expects the name {ArchivePlan.ArchiveName(table.TableName)})");
    }

    private static ShapeSuggestion? Tenant(DatabaseModel database, ProjectSettings project)
    {
        if (project.TenantTable is not null)
            return null;
        var owners = database.Tables.SelectMany(t => t.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1 && TenantNames.Any(n => n.EqualsIgnoreCase(f.ReferencingColumns[0]))
                && t.Columns.Any(c => c.Name.EqualsIgnoreCase(f.ReferencingColumns[0]) && !c.IsNullable && c.IsIntegerColumn) && !t.TableName.EqualsIgnoreCase(f.ReferencedTable)).Select(f => f.ReferencedTable))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (owners is null || owners.Count() < 2 || owners.Count() * 2 < database.Tables.Count - 1)
            return null;
        return new ShapeSuggestion("Tenant table", "TenantTable", owners.Key, project.ApiAuth,
            $"{owners.Count()} tables have a required tenant column that points at {owners.Key}{(project.ApiAuth ? "" : "; TenantTable also needs ApiAuth")}");
    }

    private static ShapeSuggestion? User(DatabaseModel database, ProjectSettings project)
    {
        if (project.UserTable is not null)
            return null;
        var users = database.Tables.Where(t => t.Columns.Any(c => c.IsStringColumn && UserTable.UserNameNames.Any(n => c.Name.EqualsIgnoreCase(n)))
            && t.Columns.Any(c => c.IsStringColumn && UserTable.PasswordHashNames.Any(n => c.Name.EqualsIgnoreCase(n)))).ToList();
        return users is [var only]
            ? new ShapeSuggestion("User table", "UserTable", only.TableName, project.ApiAuth, $"has a login name and a password hash{(project.ApiAuth ? "" : "; UserTable also needs ApiAuth")}")
            : null;
    }

    private static ShapeSuggestion? Permissions(DatabaseModel database, ProjectSettings project)
    {
        if (project.PermissionTables.Length > 0)
            return null;
        TableModel? Find(params string[] names) => database.Tables.FirstOrDefault(t => names.Any(n => t.TableName.EqualsIgnoreCase(n)));
        var role = Find("Role", "Roles");
        var permission = Find("Permission", "Permissions");
        if (role is null || permission is null)
            return null;
        bool Points(TableModel t, TableModel to) => t.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencedTable.EqualsIgnoreCase(to.TableName));
        var rolePermission = database.Tables.FirstOrDefault(t => t.IsJunctionTable && Points(t, role) && Points(t, permission));
        var userRole = database.Tables.FirstOrDefault(t => t.IsJunctionTable && Points(t, role) && !Points(t, permission));
        return rolePermission is null || userRole is null ? null
            : new ShapeSuggestion("Roles and permissions", "PermissionTables", $"{role.TableName},{permission.TableName},{rolePermission.TableName},{userRole.TableName}", project.ApiAuth && project.UserTable is not null,
                $"{role.TableName}, {permission.TableName} and their two junctions are the four tables{(project.ApiAuth && project.UserTable is not null ? "" : "; PermissionTables also needs ApiAuth and UserTable")}");
    }

    private static ShapeSuggestion? Features(DatabaseModel database, ProjectSettings project)
    {
        if (project.FeatureTables.Length > 0)
            return null;
        foreach (var flag in database.Tables.Where(t => t.PrimaryKeyColumns is [{ IsIntegerColumn: true }] && t.Columns.Any(c => c.Name.EqualsIgnoreCase("Name") && c.IsStringColumn)
            && t.Columns.Any(c => c.IsBooleanColumn && !c.IsNullable && FeatureDefaultNames.Any(n => c.Name.EqualsIgnoreCase(n)))))
        {
            var overrides = database.Tables.FirstOrDefault(t => t.ForeignKeys.Count(f => f.ReferencedTable.EqualsIgnoreCase(flag.TableName)) == 1 && t.Columns.Any(c => c.IsBooleanColumn && !c.IsNullable && c.Name.EqualsIgnoreCase("IsEnabled")));
            if (overrides is not null)
                return new ShapeSuggestion("Feature flags", "FeatureTables", $"{flag.TableName},{overrides.TableName}", project.ApiAuth, $"{flag.TableName} has a name and a default, and {overrides.TableName} refers to it with a yes/no column{(project.ApiAuth ? "" : "; FeatureTables also needs ApiAuth")}");
        }
        return null;
    }
}

/// <summary> One suggestion of <see cref="DatabaseShapeDetector.Of"/>: the table it is about (empty for the whole database) and the suggestion. </summary>
public sealed record DatabaseShapeDetection(string Table, ShapeSuggestion Suggestion);
