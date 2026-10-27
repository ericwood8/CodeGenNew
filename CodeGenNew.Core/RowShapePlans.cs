namespace CodeGenNew.Core;

/// <summary> The sentences and problem types of the row-shape routes (trees, soft delete, effective dating), the same in every stack that writes them. </summary>
public static class RowShapeWords
{
    public const string TreeCycleType = ProblemTypes.Prefix + "tree-cycle";
    public const string TreeTooDeepType = ProblemTypes.Prefix + "tree-too-deep";
    public const string TreeHasChildrenType = ProblemTypes.Prefix + "tree-has-children";
    public const string EffectiveRangeType = ProblemTypes.Prefix + "effective-range";
    public const string EffectiveOverlapType = ProblemTypes.Prefix + "effective-overlap";
    public const string NotDeletedType = ProblemTypes.Prefix + "not-deleted";

    public const string OwnParent = "A row cannot be its own parent.";
    public const string UnderItself = "A row cannot be moved under one of its own descendants.";
    public const string HasChildren = "It has child rows: move or delete them first, or promote them to its parent.";
    public const string BackwardsRange = "The valid-to date is before the valid-from date.";
    public const string Overlap = "Another row of the same kind is in effect during that period.";
    public const string NotDeleted = "Only a deleted row can be restored.";

    public static string TooDeep(int maxDepth) => $"The tree can be {maxDepth} levels deep at most.";
}

/// <summary> A self-referencing table the project lists in <c>TreeTables</c>: <c>Parent</c> is the nullable column that points at the table's own key; <c>MaxDepth</c> is 0 for no limit (a root is depth 1). </summary>
public sealed record TreeTable(TableModel Table, ColumnModel Key, ColumnModel Parent, int MaxDepth, ColumnModel? Text);

/// <summary> <c>TreeTables=Category:ParentCategoryId:4</c> (a table, optionally its parent column, optionally the deepest level allowed): a comment with a parent, a category, an organization chart. The write routes refuse a parent that is
/// the row itself or one of its descendants (a cycle) and a level past the depth limit, the delete is refused while the row has children (<c>POST {id}/promote-children</c> hands them to the row's own parent first),
/// and <c>GET tree</c> answers the rows depth first for an indented list. </summary>
public static class TreePlan
{
    public static IReadOnlyList<(string Table, string? Parent, string? MaxDepth)> Entries(ProjectSettings project) =>
        project.TreeTables.Select(item =>
        {
            string[] parts = item.Split(':', StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null, parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null);
        }).Where(e => e.Item1.Length > 0).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static TreeTable? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } tree ? tree : null;

    /// <summary> Why a listed table cannot be a tree, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    private static TreeTable? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"TreeTables: {table.TableName}";
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        var own = table.ForeignKeys.Where(f => f.ReferencedTable.EqualsIgnoreCase(table.TableName) && f.ReferencingColumns.Count == 1 && f.ReferencedColumns.Count == 1
            && f.ReferencedColumns[0].EqualsIgnoreCase(key.Name)).ToList();
        string? parentName = entry.Parent ?? (own.Count == 1 ? own[0].ReferencingColumns[0] : null);
        if (parentName is null)
        {
            problem = own.Count == 0
                ? $"{prefix} has no foreign key to its own key. Name the parent column (TreeTables={table.TableName}:ParentId) after adding the key."
                : $"{prefix} has more than one foreign key to itself ({string.Join(", ", own.Select(f => f.ReferencingColumns[0]))}): name the parent column (TreeTables={table.TableName}:{own[0].ReferencingColumns[0]}).";
            return null;
        }
        var parent = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(parentName));
        if (parent is null || !parent.IsIntegerColumn || !parent.IsNullable || parent.Name.EqualsIgnoreCase(key.Name))
        {
            problem = $"{prefix}: {parentName} must be a whole-number column that allows NULL (a root has no parent) and is not the key.";
            return null;
        }
        int maxDepth = 0;
        if (entry.MaxDepth is { } depthText && !(int.TryParse(depthText, out maxDepth) && maxDepth >= 1))
        {
            problem = $"{prefix}: the depth limit {depthText} must be a whole number of 1 or more.";
            return null;
        }
        var text = table.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && (c.Name.EqualsIgnoreCase("Name") || c.Name.EqualsIgnoreCase("Title")))
            ?? table.DisplayColumns.FirstOrDefault(c => c.IsStringColumn);
        return new TreeTable(table, key, parent, maxDepth, text);
    }
}

/// <summary> A table whose rows are flagged instead of removed (<c>SoftDeleteTables=Customer</c> or <c>Customer:IsRemoved</c>). <c>DeletedOn</c> and <c>DeletedBy</c> are optional and filled when the table has them. </summary>
public sealed record SoftDeleteTable(TableModel Table, ColumnModel Key, ColumnModel Flag, ColumnModel? DeletedOn, ColumnModel? DeletedBy);

/// <summary> <c>SoftDeleteTables=Customer</c>: the delete route sets the flag (<c>IsDeleted</c>, else the column named after the colon) and the stamps instead of removing the row, the context filters flagged rows out of every list, look-up and
/// read, <c>GET deleted</c> lists them and <c>POST {id}/restore</c> brings one back. </summary>
public static class SoftDeletePlan
{
    public const string DefaultFlag = "IsDeleted";

    public static IReadOnlyList<(string Table, string Flag)> Entries(ProjectSettings project) =>
        project.SoftDeleteTables.Select(item =>
        {
            string[] parts = item.Split(':', StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : DefaultFlag);
        }).Where(e => e.Item1.Length > 0).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static SoftDeleteTable? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } soft ? soft : null;

    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    private static SoftDeleteTable? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"SoftDeleteTables: {table.TableName}";
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        var flag = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.Flag));
        if (flag is not { IsBooleanColumn: true, IsNullable: false })
        {
            problem = $"{prefix} needs a yes/no column {entry.Flag} that cannot be NULL (false = in use).";
            return null;
        }
        var on = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase("DeletedOn") && LogTablePlan.HasTimeOfDay(c) && c.IsNullable);
        var by = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase("DeletedBy") && c.IsStringColumn && c.IsNullable);
        return new SoftDeleteTable(table, key, flag, on, by);
    }
}

/// <summary> A table whose rows are valid for a period (<c>EffectiveDatedTables=Price:ValidFrom:ValidTo:ItemId</c>: the first and the last day, then the columns that say what the row is a price of). <c>To</c> allows NULL for an open end.
/// <c>Groups</c> are those columns (default: the table's single-column foreign keys); rows of one group may not overlap. </summary>
public sealed record EffectiveTable(TableModel Table, ColumnModel Key, ColumnModel From, ColumnModel To, IReadOnlyList<ColumnModel> Groups);

/// <summary> <c>EffectiveDatedTables=Price:ValidFrom:ValidTo[:ItemId+CurrencyId]</c>: a save is refused when the period runs backwards or overlaps another row of the same group, and <c>GET current?on=</c> answers the rows in effect on a
/// date. The end is inclusive; an empty end is open. </summary>
public static class EffectiveDatePlan
{
    public static IReadOnlyList<(string Table, string? From, string? To, string? Groups)> Entries(ProjectSettings project) =>
        project.EffectiveDatedTables.Select(item =>
        {
            string[] parts = item.Split(':', StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null, parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null, parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null);
        }).Where(e => e.Item1.Length > 0).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static EffectiveTable? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } effective ? effective : null;

    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, out string? problem);
        return problem;
    }

    private static EffectiveTable? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"EffectiveDatedTables: {table.TableName}";
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        if (entry.From is null || entry.To is null)
        {
            problem = $"{prefix} needs the first and last day columns (EffectiveDatedTables={table.TableName}:ValidFrom:ValidTo).";
            return null;
        }
        var from = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.From));
        var to = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(entry.To));
        if (from is not { IsDateColumn: true, IsNullable: false } || to is not { IsDateColumn: true } || from.SqlType != to.SqlType)
        {
            problem = $"{prefix}: {entry.From} (cannot be NULL) and {entry.To} must be date columns of the same type.";
            return null;
        }
        List<ColumnModel> groups;
        if (entry.Groups is { } named)
        {
            groups = [];
            foreach (string name in named.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var column = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
                if (column is null)
                {
                    problem = $"{prefix}: {name} is not a column of the table.";
                    return null;
                }
                groups.Add(column);
            }
        }
        else
        {
            groups = table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1).Select(f => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(f.ReferencingColumns[0])))
                .OfType<ColumnModel>().Where(c => !c.Name.EqualsIgnoreCase(from.Name) && !c.Name.EqualsIgnoreCase(to.Name)).DistinctBy(c => c.Name).ToList();
        }
        return new EffectiveTable(table, key, from, to, groups);
    }
}

/// <summary> Whether a table has any row shape (tree, soft delete, effective dating) and the reasons the project's lists cannot be served. </summary>
public static class RowShapePlan
{
    public static bool Any(ProjectSettings project) => project.TreeTables.Length > 0 || project.SoftDeleteTables.Length > 0 || project.EffectiveDatedTables.Length > 0;

    public static bool Has(TableModel table, ProjectSettings project) =>
        TreePlan.For(table, project) is not null || SoftDeletePlan.For(table, project) is not null || EffectiveDatePlan.For(table, project) is not null;

    /// <summary> Every reason the three settings cannot be served by <paramref name="database"/>. </summary>
    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        foreach (var table in database.Tables)
        {
            foreach (string? problem in new[] { TreePlan.Problem(table, project), SoftDeletePlan.Problem(table, project), EffectiveDatePlan.Problem(table, project) })
                if (problem is not null)
                    problems.Add(problem);
        }
        problems.AddRange(TreePlan.Entries(project).Where(e => !database.Tables.Any(t => t.TableName.EqualsIgnoreCase(e.Table))).Select(e => $"TreeTables: {e.Table} is not a table of the database."));
        problems.AddRange(SoftDeletePlan.Entries(project).Where(e => !database.Tables.Any(t => t.TableName.EqualsIgnoreCase(e.Table))).Select(e => $"SoftDeleteTables: {e.Table} is not a table of the database."));
        problems.AddRange(EffectiveDatePlan.Entries(project).Where(e => !database.Tables.Any(t => t.TableName.EqualsIgnoreCase(e.Table))).Select(e => $"EffectiveDatedTables: {e.Table} is not a table of the database."));
        if (Any(project) && project.AccessModeFor(database.Dialect) != AccessMode.Ef)
            problems.Add("TreeTables, SoftDeleteTables and EffectiveDatedTables need AccessMode=Ef.");
        return problems;
    }
}
