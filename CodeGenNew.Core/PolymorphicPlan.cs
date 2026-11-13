namespace CodeGenNew.Core;

/// <summary> The words of a polymorphic link table (a record type name and a record id that may name a row of any listed table): the problem types and sentences, the same in every stack that writes them. </summary>
public static class LinkWords
{
    public const string UnknownTypeType = ProblemTypes.Prefix + "link-type-unknown";
    public const string TargetMissingType = ProblemTypes.Prefix + "link-target-missing";
    public const string TargetInUseType = ProblemTypes.Prefix + "link-target-in-use";

    public static string UnknownType(IEnumerable<string> names) => "The record type must be one of: " + string.Join(", ", names) + ".";

    public static string TargetMissing(string type) => $"There is no {type} with that id.";

    public static string InUse(string linkTable) => $"It still has rows in {linkTable}: remove them first.";
}

/// <summary> One table a link may name: <c>Table</c> is the name stored in the type column and <c>Key</c> the whole-number key the id column holds. </summary>
public sealed record LinkTarget(TableModel Table, ColumnModel Key);

/// <summary> A table the project lists in <c>PolymorphicLinks</c>: <c>TypeColumn</c> holds the name of a table in <c>Targets</c> and <c>IdColumn</c> the key of a row of it. <c>RemoveWithTarget</c> is true when deleting a
/// target row removes its links; false (the default) refuses the delete while a link exists. </summary>
public sealed record LinkShape(TableModel Table, ColumnModel Key, ColumnModel TypeColumn, ColumnModel IdColumn, IReadOnlyList<LinkTarget> Targets, bool RemoveWithTarget);

/// <summary> A table that some link table may name: one entry for each link table that lists it. </summary>
public sealed record LinkedShape(TableModel Table, ColumnModel Key, IReadOnlyList<LinkShape> Links);

/// <summary> <c>PolymorphicLinks=Attachment:OwnerType:OwnerId:Product|Opportunity|Customer</c> (the link table, its type column, its id column, then the tables the type may name, with <c>:remove</c> added to remove the
/// links of a deleted row instead of refusing the delete): a brochure, a comment or a note that attaches to a row of any of several tables. A foreign key cannot hold that, so the checks are here: a save whose type names no listed
/// table, or whose id is not a row of the table it names, is refused (400); the type is written in the table's own spelling; a delete of a listed table's row is refused while links name it (409), or removes them. </summary>
public static class PolymorphicPlan
{
    public const string RemoveWord = "remove";

    public static IReadOnlyList<(string Table, string? Type, string? Id, string[] Targets, bool Remove)> Entries(ProjectSettings project) =>
        SettingParts.Items(project.PolymorphicLinks).Select(p => (p[0], SettingParts.At(p, 1), SettingParts.At(p, 2),
            (SettingParts.At(p, 3) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            SettingParts.At(p, 4)?.EqualsIgnoreCase(RemoveWord) == true)).ToList();

    public static bool IsLinkTable(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static bool IsTarget(string table, ProjectSettings project) => Entries(project).Any(e => e.Targets.Any(t => t.EqualsIgnoreCase(table)));

    /// <summary> Whether a delete of a row of <paramref name="table"/> is refused while links name it (a link table without <c>:remove</c> lists it). </summary>
    public static bool RefusesDeleteOf(string table, ProjectSettings project) => Entries(project).Any(e => !e.Remove && e.Targets.Any(t => t.EqualsIgnoreCase(table)));

    /// <summary> Whether a delete of a row of <paramref name="table"/> removes the links that name it (<c>:remove</c>), so the API has to run <c>AfterDeleteAsync</c>. </summary>
    public static bool RemovesLinksOf(string table, ProjectSettings project) => Entries(project).Any(e => e.Remove && e.Targets.Any(t => t.EqualsIgnoreCase(table)));

    /// <summary> The link table's plan, or null when the table is not listed or cannot be one (<see cref="Problems"/> says why). </summary>
    public static LinkShape? LinkOf(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsLinkTable(table.TableName, project) && Resolve(table, project, database, out _) is { } link ? link : null;

    /// <summary> The links that name rows of <paramref name="table"/>, or null when no link table lists it. </summary>
    public static LinkedShape? TargetOf(TableModel table, ProjectSettings project, DatabaseModel database)
    {
        if (!IsTarget(table.TableName, project))
            return null;
        var links = database.Tables.Select(t => LinkOf(t, project, database)).OfType<LinkShape>()
            .Where(l => l.Targets.Any(x => x.Table.TableName.EqualsIgnoreCase(table.TableName))).ToList();
        return links.Count == 0 || table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key] ? null : new LinkedShape(table, key, links);
    }

    /// <summary> Makes the record type column of every link table a column that lists the names of the tables it may hold, as a column with a CHECK list is, so the forms of every front end show a drop-down of them,
    /// the validators name them and the generated tests take the first. A column that already lists values keeps its own. </summary>
    public static void ApplyChoices(DatabaseModel database, ProjectSettings project)
    {
        foreach (var table in database.Tables)
            ApplyChoices(table, database, project);
    }

    /// <summary> The same for one table, such as a model read again with its parents' display columns. </summary>
    public static void ApplyChoices(TableModel table, DatabaseModel database, ProjectSettings project)
    {
        if (LinkOf(table, project, database) is { } link && !link.TypeColumn.HasChoices)
            link.TypeColumn.Choices = link.Targets.Select(t => t.Table.TableName).ToList();
        AddGrids(table, project, database);
    }

    // the grid's rows open on the link table's own screen, so a link table without one (too small, or left out of Screens) has no grid
    private static bool HasScreen(TableModel link, ProjectSettings project) =>
        project.Screens.Length > 0 ? project.Screens.Contains(link.TableName, StringComparer.OrdinalIgnoreCase) : link.HasSearchApi(project);

    /// <summary> Gives every listed table a child grid for each link table that names it (the "attachments" of a row): a child the foreign keys do not show, since there is none. The grid keeps the link table's rows
    /// whose record type is this table and whose id is the row; the web and desktop screens read it as they read any child. </summary>
    private static void AddGrids(TableModel table, ProjectSettings project, DatabaseModel database)
    {
        if (TargetOf(table, project, database) is not { } linked)
            return;
        foreach (var link in linked.Links)
        {
            string constraint = "polymorphic:" + link.Table.TableName;
            if (table.ChildForeignKeys.Any(c => c.ConstraintName == constraint) || !HasScreen(link.Table, project))
                continue;
            table.ChildForeignKeys.Add(new ChildForeignKeyModel
            {
                ConstraintName = constraint,
                ReferencingSchema = link.Table.SchemaName,
                ReferencingTable = link.Table.TableName,
                ReferencingDatabaseTable = link.Table.DbTableName == link.Table.TableName ? null : link.Table.DbTableName,
                ReferencingColumns = [link.IdColumn.Name],
                ReferencedColumns = [linked.Key.Name],
                ReferencingPrimaryKeyColumns = link.Table.PrimaryKeyColumns.Select(c => c.Name).ToList(),
                ReferencingTableForeignKeys = link.Table.ForeignKeys.ToList(),
                ReferencingTableColumns = link.Table.Columns.ToList(),
                TypeColumn = link.TypeColumn.Name,
                TypeValue = table.TableName
            });
        }
    }

    /// <summary> Every reason the setting cannot be served by <paramref name="database"/>. </summary>
    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var entry in Entries(project))
        {
            var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(entry.Table));
            if (table is null)
                problems.Add($"PolymorphicLinks: {entry.Table} is not a table of the database.");
            else if (Resolve(table, project, database, out string? problem) is null && problem is not null)
                problems.Add(problem);
        }
        return problems;
    }

    private static LinkShape? Resolve(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"PolymorphicLinks: {table.TableName}";
        string syntax = $"PolymorphicLinks={table.TableName}:TypeColumn:IdColumn:Table1|Table2";
        if (RowShapeWords.NeverChanged(table, project) is { } never)
        {
            problem = $"{prefix} {never}, so nothing can be linked.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        var type = entry.Type is null ? null : table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.Type));
        var id = entry.Id is null ? null : table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.Id));
        if (type is not { IsStringColumn: true, IsPrimaryKey: false })
        {
            problem = $"{prefix} needs a text column for the record type ({syntax}).";
            return null;
        }
        if (id is not { IsIntegerColumn: true, IsPrimaryKey: false })
        {
            problem = $"{prefix} needs a whole-number column for the record id ({syntax}).";
            return null;
        }
        if (table.ForeignKeys.Any(f => f.ReferencingColumns.Any(c => c.EqualsIgnoreCase(id.Name))))
        {
            problem = $"{prefix}: {id.Name} has a foreign key, so it names one table. A polymorphic id has none.";
            return null;
        }
        if (entry.Targets.Length < 2)
        {
            problem = $"{prefix} needs at least two tables the type may name ({syntax}); one table is an ordinary foreign key.";
            return null;
        }
        var targets = new List<LinkTarget>();
        foreach (string name in entry.Targets)
        {
            var target = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name));
            if (target is null)
            {
                problem = $"{prefix}: {name} is not a table of the database.";
                return null;
            }
            if (target.TableName.EqualsIgnoreCase(table.TableName))
            {
                problem = $"{prefix} cannot link to itself.";
                return null;
            }
            if (target.PrimaryKeyColumns is not [{ IsIntegerColumn: true } targetKey])
            {
                problem = $"{prefix}: {target.TableName} needs a single whole-number key to be named by an id.";
                return null;
            }
            if (targets.Any(t => t.Table.TableName.EqualsIgnoreCase(target.TableName)))
            {
                problem = $"{prefix} lists {target.TableName} twice.";
                return null;
            }
            if (entry.Remove && project.SoftDeleteTables.Any(t => t.Split(':')[0].Trim().EqualsIgnoreCase(target.TableName)))
            {
                problem = $"{prefix}: {target.TableName} is in SoftDeleteTables, whose delete keeps the row: leave out :{RemoveWord} so its links stay.";
                return null;
            }
            if (entry.Remove && AssetPlan.IsListed(target.TableName, project))
            {
                problem = $"{prefix}: {target.TableName} is also in AssetTables: its delete removes a file, and :{RemoveWord} would remove links too. Leave out :{RemoveWord}.";
                return null;
            }
            targets.Add(new LinkTarget(target, targetKey));
        }
        int longest = targets.Max(t => t.Table.TableName.Length);
        if (type.CharacterLength is > 0 && type.CharacterLength < longest)
        {
            problem = $"{prefix}: {type.Name} holds {type.CharacterLength} characters; the longest table name needs {longest}.";
            return null;
        }
        if (entry.Remove && AssetPlan.IsListed(table.TableName, project))
        {
            problem = $"{prefix} is also in AssetTables: removing its rows would leave their files in the store. Leave out :{RemoveWord}.";
            return null;
        }
        return new LinkShape(table, key, type, id, targets, entry.Remove);
    }
}
