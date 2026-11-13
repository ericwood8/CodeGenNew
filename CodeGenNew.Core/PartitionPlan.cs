namespace CodeGenNew.Core;

/// <summary> One table a script splits into a partition for each month of <c>Date</c>. <c>Key</c> is the primary key the script extends with the date, <c>Indexes</c> the other indexes it recreates on the partition scheme and
/// <c>LooseUnique</c> the names of the unique indexes that lack the date (a database cannot enforce them across partitions, so the script makes them plain indexes). </summary>
public sealed record PartitionJob(TableModel Table, ColumnModel Date, IReadOnlyList<ColumnModel> Key, IReadOnlyList<IndexModel> Indexes, IReadOnlyList<string> LooseUnique)
{
    public string ProcedureName => PartitionPlan.ProcedureName(Table);
}

public sealed record PartitionResolution(IReadOnlyList<PartitionJob> Jobs, IReadOnlyList<string> Problems);

/// <summary> <c>PartitionTables=WebhookEvent,PageView:ViewedOn</c>: the tables a script converts to one partition for each month of a date column (found as for <c>ArchiveTables</c> when left out) and a procedure that creates the
/// coming months and drops the months older than <c>PartitionKeepMonths</c>. The date joins the primary key, since a partitioned table cannot enforce a key that lacks the partition column, so the generator reads the table
/// before the script has run (a table with that composite key already is the converted one). The script is written for the project to review and run; the application never runs it. </summary>
public static class PartitionPlan
{
    public static string ProcedureName(TableModel table) => "Partition" + table.TableName;

    public static IReadOnlyList<(string Table, string? Date)> Entries(ProjectSettings project) =>
        SettingParts.Items(project.PartitionTables).Select(p => (p[0], SettingParts.At(p, 1))).ToList();

    private static ColumnModel? DateOf(TableModel table, ProjectSettings project, string? named)
    {
        if (named is not null)
            return table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(named));
        var shapes = TableShapes.For(table, project);
        return shapes.Inbound?.Received ?? shapes.Usage?.Occurred ?? (LogTablePlan.IsListed(table, project) ? shapes.Log?.Timestamp : null) ?? LogTablePlan.Guess(table);
    }

    /// <summary> The table has the composite key (a key column and the date column) the script gives a table, so it was probably converted already. </summary>
    public static bool LooksPartitioned(TableModel table, ProjectSettings project)
    {
        var entry = Entries(project).FirstOrDefault(e => e.Table.EqualsIgnoreCase(table.TableName));
        if (entry.Table is null || table.PrimaryKeyColumns.Count != 2)
            return false;
        var date = table.Columns.FirstOrDefault(c => entry.Date is null ? LogTablePlan.HasTimeOfDay(c) : c.Name.EqualsIgnoreCase(entry.Date));
        return date is not null && table.PrimaryKeyColumns.Any(c => c.Name.EqualsIgnoreCase(date.Name));
    }

    public static PartitionResolution Resolve(DatabaseModel database, ProjectSettings project)
    {
        var jobs = new List<PartitionJob>();
        var problems = new List<string>();
        foreach (var (name, dateName) in Entries(project))
        {
            string prefix = $"PartitionTables: {name}";
            var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name));
            if (table is null)
            {
                problems.Add($"{prefix} is not a table of the database.");
                continue;
            }
            if (database.Dialect == SqlDialect.Sqlite)
            {
                problems.Add($"{prefix}: SQLite has no partitions.");
                continue;
            }
            if (table.PrimaryKeyColumns.Count == 0)
            {
                problems.Add($"{prefix} needs a primary key.");
                continue;
            }
            var date = DateOf(table, project, dateName);
            if (date is null || !LogTablePlan.HasTimeOfDay(date))
            {
                problems.Add(dateName is null ? $"{prefix} has no date and time column to split by (name it: {name}:ColumnName)." : $"{prefix}: {dateName} is not a date and time column of the table.");
                continue;
            }
            if (date.IsNullable)
            {
                problems.Add($"{prefix}: {date.Name} must not allow NULL, since it joins the primary key.");
                continue;
            }
            if (table.PrimaryKeyColumns.Any(c => c.Name.EqualsIgnoreCase(date.Name)))
            {
                problems.Add($"{prefix}: the key already holds {date.Name}; the script runs against a table whose key is the key column alone.");
                continue;
            }
            if (table.ForeignKeys.Count > 0 || table.ChildForeignKeys.Any(c => !c.IsPolymorphic))
            {
                problems.Add($"{prefix} has foreign keys (to or from it); a partitioned table cannot keep them in every database.");
                continue;
            }
            if (table.PartialIndexes.Count > 0)
            {
                problems.Add($"{prefix} has filtered indexes ({string.Join(", ", table.PartialIndexes.Select(i => i.Name))}); the script cannot move them onto the partitions.");
                continue;
            }
            var key = table.PrimaryKeyColumns.ToList();
            bool IsKey(IndexModel i) => i.IsUnique && i.Columns.Count == key.Count && i.Columns.All(c => key.Any(k => k.DbName.EqualsIgnoreCase(c)));
            var others = table.Indexes.Where(i => !IsKey(i)).ToList();
            var loose = others.Where(i => i.IsUnique && !i.Columns.Any(c => c.EqualsIgnoreCase(date.DbName))).Select(i => i.Name).ToList();
            jobs.Add(new PartitionJob(table, date, key, others, loose));
        }
        return new PartitionResolution(jobs, problems);
    }
}
