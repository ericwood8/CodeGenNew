namespace CodeGenNew.Core;

/// <summary> One copied column: <c>Target</c> of the child row takes the value of <c>Source</c> in the parent row that <c>Link</c> points at. </summary>
public sealed record SnapshotCopy(ColumnModel Target, ColumnModel Link, TableModel Parent, ColumnModel ParentKey, ColumnModel Source);

/// <summary> The line total of a snapshot table: <c>Target = FactorA * FactorB</c>, computed by the server. </summary>
public sealed record SnapshotTotal(ColumnModel Target, ColumnModel FactorA, ColumnModel FactorB);

/// <summary> A child table that keeps what its parents looked like when the row was written. </summary>
public sealed record SnapshotShape(TableModel Table, ColumnModel Key, IReadOnlyList<SnapshotCopy> Copies, SnapshotTotal? Total)
{
    /// <summary> The columns of the row that the server owns (the copies and the total). </summary>
    public IEnumerable<ColumnModel> Owned => Copies.Select(c => c.Target).Concat(Total is null ? [] : [Total.Target]);
}

/// <summary> <c>SnapshotColumns=OrderItem:ProductName&lt;-Product.Name+UnitPrice&lt;-Product.Price:LineTotal=Quantity*UnitPrice</c>: on create the server copies the named columns of the parent row into the child row, on update it puts the
/// stored copies back (a change is ignored), and the line total is worked out from the factors on every save. </summary>
public static class SnapshotPlan
{
    public sealed record Entry(string Table, string? Copies, string? Total);

    public static IReadOnlyList<Entry> Entries(ProjectSettings project) =>
        SettingParts.Items(project.SnapshotColumns).Select(p => new Entry(p[0], SettingParts.At(p, 1), SettingParts.At(p, 2))).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> Whether <paramref name="column"/> is one the server fills (a copy or the total); the forms show it as text. Works from the setting alone. </summary>
    public static bool IsOwnedColumn(TableModel table, ProjectSettings project, ColumnModel column)
    {
        if (Entries(project).FirstOrDefault(e => e.Table.EqualsIgnoreCase(table.TableName)) is not { } entry)
            return false;
        foreach (string copy in (entry.Copies ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (copy.Split("<-", 2, StringSplitOptions.TrimEntries)[0].EqualsIgnoreCase(column.Name))
                return true;
        return entry.Total is not null && entry.Total.Split('=', 2, StringSplitOptions.TrimEntries)[0].EqualsIgnoreCase(column.Name);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectSettings, DatabaseModel> Known = new();

    /// <summary> Keeps the database of a run for the templates that see one table at a time (the Python and Rust routes), which read the parents of a snapshot through <see cref="For(TableModel, ProjectSettings)"/>. </summary>
    public static void Remember(DatabaseModel database, ProjectSettings project)
    {
        if (project.SnapshotColumns.Length == 0)
            return;
        Known.Remove(project);
        Known.Add(project, database);
    }

    /// <summary> The snapshot of a table when the generator remembered the database of the run; null when it did not (a table run alone). </summary>
    public static SnapshotShape? For(TableModel table, ProjectSettings project) =>
        Known.TryGetValue(project, out var database) ? For(table, project, database) : null;

    public static SnapshotShape? For(TableModel table, ProjectSettings project, DatabaseModel database) =>
        IsListed(table.TableName, project) && Resolve(table, project, database, out _) is { } shape ? shape : null;

    public static string? Problem(TableModel table, ProjectSettings project, DatabaseModel database)
    {
        if (!IsListed(table.TableName, project))
            return null;
        Resolve(table, project, database, out string? problem);
        return problem;
    }

    private static SnapshotShape? Resolve(TableModel table, ProjectSettings project, DatabaseModel database, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"SnapshotColumns: {table.TableName}";
        string syntax = $"SnapshotColumns={table.TableName}:Column<-Parent.Column+Column<-Parent.Column:Total=FactorA*FactorB";
        if (RowShapeWords.NeverChanged(table, project) is { } never)
        {
            problem = $"{prefix} {never}, so it has no snapshot to keep.";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ } key])
        {
            problem = $"{prefix} needs a single-column key.";
            return null;
        }
        if (entry.Copies is null)
        {
            problem = $"{prefix} needs the columns to copy ({syntax}).";
            return null;
        }
        var copies = new List<SnapshotCopy>();
        foreach (string text in entry.Copies.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] sides = text.Split("<-", 2, StringSplitOptions.TrimEntries);
            string[] from = sides.Length == 2 ? sides[1].Split('.', 2, StringSplitOptions.TrimEntries) : [];
            if (from.Length != 2 || from[0].Length == 0 || from[1].Length == 0)
            {
                problem = $"{prefix}: {text} is not Column<-Parent.Column ({syntax}).";
                return null;
            }
            var target = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(sides[0]));
            if (target is null)
            {
                problem = $"{prefix}: {sides[0]} is not a column of the table.";
                return null;
            }
            if (target.IsPrimaryKey || target.IsIdentity || target.IsComputed)
            {
                problem = $"{prefix}: {target.Name} is the key or is worked out by the database, so it cannot be a copy.";
                return null;
            }
            var parent = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(from[0]));
            if (parent is null)
            {
                problem = $"{prefix}: {from[0]} is not a table of the database.";
                return null;
            }
            var links = table.ForeignKeys.Where(f => f.ReferencedTable.EqualsIgnoreCase(parent.TableName) && f.ReferencingColumns.Count == 1).ToList();
            if (links.Count != 1)
            {
                problem = $"{prefix} needs exactly one single-column foreign key to {parent.TableName} to copy from it ({links.Count} found).";
                return null;
            }
            var link = table.Columns.First(c => c.Name.EqualsIgnoreCase(links[0].ReferencingColumns[0]));
            if (target.Name.EqualsIgnoreCase(link.Name))
            {
                problem = $"{prefix}: {target.Name} is the foreign key to {parent.TableName}, so it cannot be a copy.";
                return null;
            }
            var source = parent.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(from[1]));
            if (source is null)
            {
                problem = $"{prefix}: {from[1]} is not a column of {parent.TableName}.";
                return null;
            }
            var parentKey = parent.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(links[0].ReferencedColumns[0]));
            if (parentKey is null)
            {
                problem = $"{prefix}: the foreign key to {parent.TableName} does not name a column of it.";
                return null;
            }
            if (!target.HasSameTypeAs(source) || source.IsNullable && !target.IsNullable || target.IsStringColumn && source.CharacterLength > target.CharacterLength && target.CharacterLength > 0)
            {
                problem = $"{prefix}: {target.Name} cannot hold {parent.TableName}.{source.Name} (the type must match, the column must allow NULL when the source does, and a text column must be as long).";
                return null;
            }
            if (copies.Any(c => c.Target.Name.EqualsIgnoreCase(target.Name)))
            {
                problem = $"{prefix}: {target.Name} is copied twice.";
                return null;
            }
            copies.Add(new SnapshotCopy(target, link, parent, parentKey, source));
        }
        if (copies.Count == 0)
        {
            problem = $"{prefix} needs the columns to copy ({syntax}).";
            return null;
        }
        SnapshotTotal? total = null;
        if (entry.Total is not null)
        {
            string[] sides = entry.Total.Split('=', 2, StringSplitOptions.TrimEntries);
            string[] factors = sides.Length == 2 ? sides[1].Split('*', StringSplitOptions.TrimEntries) : [];
            if (factors.Length != 2)
            {
                problem = $"{prefix}: the total {entry.Total} is not Column=FactorA*FactorB ({syntax}).";
                return null;
            }
            var columns = new[] { sides[0], factors[0], factors[1] }.Select(n => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(n))).ToList();
            if (columns.Any(c => c is null))
            {
                problem = $"{prefix}: {string.Join(", ", new[] { sides[0], factors[0], factors[1] }.Where((n, i) => columns[i] is null))} is not a column of the table.";
                return null;
            }
            if (columns.Any(c => !(c!.IsNumericColumn || c.IsIntegerColumn || c.IsMoneyColumn)) || columns[0]!.IsNullable || columns[0]!.IsComputed || columns[0]!.IsPrimaryKey || copies.Any(c => c.Target.Name.EqualsIgnoreCase(columns[0]!.Name)))
            {
                problem = $"{prefix}: the total and its factors must be number columns, and the total cannot be NULL, a copy, the key or worked out by the database.";
                return null;
            }
            total = new SnapshotTotal(columns[0]!, columns[1]!, columns[2]!);
        }
        return new SnapshotShape(table, key, copies, total);
    }
}
