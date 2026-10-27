namespace CodeGenNew.Core;

/// <summary> One table a script keeps small: the rows older than <c>Days</c> (by <c>Age</c>) move to the archive table, or are deleted. A work queue only gives up its finished rows. <c>Archive</c> is the table the database
/// already has (null: the script creates it). <c>Copied</c> are the source columns the archive has, in the source's order. </summary>
public sealed record ArchiveJob(TableModel Table, int Days, bool Delete, ColumnModel Age, QueueTable? Queue, TableModel? Archive, IReadOnlyList<ColumnModel> Copied, ColumnModel? ArchivedOn)
{
    public string ArchiveName => ArchivePlan.ArchiveName(Table.TableName);
}

/// <summary> The result of <see cref="ArchivePlan.Resolve"/>: the jobs the script covers and the reasons for the tables it cannot. </summary>
public sealed record ArchiveResolution(IReadOnlyList<ArchiveJob> Jobs, IReadOnlyList<string> Problems);

/// <summary> <c>ArchiveTables=ErrorLog:90,JobQueue:30:delete</c>: the tables a scheduled script keeps small, the days of rows to keep and, after <c>delete</c>, that the old rows are removed instead of moved to
/// <c>&lt;Table&gt;Archive</c>. The script is written for the project to review and schedule; the application never runs it. An archive table the database has is shown as a read-only list (<see cref="LogTablePlan"/>). </summary>
public static class ArchivePlan
{
    public const string Suffix = "Archive";
    public const string ArchivedOnName = "ArchivedOn";

    public static string ArchiveName(string table) => table + Suffix;

    public static IReadOnlyList<(string Table, string Days, bool Delete)> Entries(ProjectSettings project) =>
        project.ArchiveTables.Select(item =>
        {
            string[] parts = item.Split(':', StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 ? parts[1] : "", parts.Length > 2 && parts[2].EqualsIgnoreCase("delete"));
        }).Where(e => e.Item1.Length > 0).ToList();

    /// <summary> The archive tables the project's entries imply, so each can be shown as a read-only list. </summary>
    public static IEnumerable<string> ArchiveTableNames(ProjectSettings project) =>
        Entries(project).Where(e => !e.Delete).Select(e => ArchiveName(e.Table));

    public static ArchiveResolution Resolve(DatabaseModel database, ProjectSettings project)
    {
        var jobs = new List<ArchiveJob>();
        var problems = new List<string>();
        if (project.ArchiveTables.Length > 0 && project.ArchiveRunTime is null)
            problems.Add("ArchiveRunAt is not a time of day (use HH:mm in 24 hours, for example 02:00).");
        foreach (var (name, daysText, delete) in Entries(project))
        {
            string prefix = $"ArchiveTables: {name}";
            var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name));
            if (table is null)
            {
                problems.Add($"{prefix} is not a table of the database.");
                continue;
            }
            if (!int.TryParse(daysText, out int days) || days < 1)
            {
                problems.Add($"{prefix} needs the days of rows to keep (ArchiveTables={name}:90, or {name}:90:delete).");
                continue;
            }
            if (table.PrimaryKeyColumns.Count == 0)
            {
                problems.Add($"{prefix} needs a primary key, so each batch moves rows once.");
                continue;
            }
            var queue = QueuePlan.For(table, project);
            var age = queue?.Processed ?? (LogTablePlan.IsListed(table, project) ? LogTablePlan.For(table, project)?.Timestamp : null) ?? LogTablePlan.Guess(table);
            if (age is null)
            {
                problems.Add($"{prefix} has no date and time column to measure age by.");
                continue;
            }
            string archiveName = ArchiveName(table.TableName);
            var archive = delete ? null : database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(archiveName));
            var copied = table.Columns.Where(c => !c.IsComputed).ToList();
            ColumnModel? archivedOn = null;
            if (archive is not null)
            {
                var missing = copied.Where(c => !archive.Columns.Any(a => a.Name.EqualsIgnoreCase(c.Name))).Select(c => c.Name).ToList();
                var identity = database.Dialect != SqlDialect.SqlServer ? new List<string>() : archive.Columns.Where(a => a.IsIdentity && copied.Any(c => c.Name.EqualsIgnoreCase(a.Name))).Select(a => a.Name).ToList();
                if (missing.Count > 0 || identity.Count > 0)
                {
                    problems.Add(missing.Count > 0
                        ? $"{prefix}: {archive.TableName} lacks the column {string.Join(", ", missing)} of {table.TableName}."
                        : $"{prefix}: {archive.TableName}.{identity[0]} is an identity column; the archive keeps the original key, and SQL Server does not move rows into an identity column.");
                    continue;
                }
                copied = copied.Where(c => archive.Columns.Any(a => a.Name.EqualsIgnoreCase(c.Name))).ToList();
                archivedOn = archive.Columns.FirstOrDefault(a => a.Name.EqualsIgnoreCase(ArchivedOnName) && LogTablePlan.HasTimeOfDay(a));
            }
            jobs.Add(new ArchiveJob(table, days, delete, age, queue, archive, copied, archivedOn));
        }
        return new ArchiveResolution(jobs, problems);
    }
}
