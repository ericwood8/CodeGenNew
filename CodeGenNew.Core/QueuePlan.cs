namespace CodeGenNew.Core;

/// <summary> The values of the status column of a work-queue row. </summary>
public static class QueueStatus
{
    public const string Pending = "pending";
    public const string Claimed = "claimed";
    public const string Done = "done";
    public const string Failed = "failed";

    public static readonly string[] All = [Pending, Claimed, Done, Failed];
}

/// <summary> A work-queue table and the columns the routes use. <c>LastError</c> and <c>ClaimedBy</c> are optional; the others must exist. </summary>
public sealed record QueueTable(TableModel Table, ColumnModel Key, ColumnModel Status, ColumnModel Retry, ColumnModel Created, ColumnModel Claimed, ColumnModel Processed, ColumnModel? ClaimedBy, ColumnModel? LastError)
{
    /// <summary> The columns a producer supplies when it puts a row in the queue: those that cannot be NULL, without the ones the queue itself sets (status, retries, the stamps, the worker, the error) and a key the database numbers. </summary>
    public IReadOnlyList<ColumnModel> Payload => ApiSampleValues.BodyColumns(Table).Where(c => !IsManaged(c) && !(c.IsPrimaryKey && c.IsIdentity)).ToList();

    private bool IsManaged(ColumnModel c) => new[] { Status, Retry, Created, Claimed, Processed, ClaimedBy, LastError }.Any(m => m is not null && m.Name.EqualsIgnoreCase(c.Name));
}

/// <summary> Which tables are work queues (<c>QueueTables=JobQueue,Mail:State:Attempts</c>: a table and optionally its status and retry columns, default Status and RetryCount). A queue row is pending, claimed, done or failed;
/// a worker takes the oldest pending row with one guarded UPDATE, then completes it or fails it (one more retry, back to pending until <c>QueueMaxRetries</c>, then failed). A claim nobody completed is released after
/// <c>QueueClaimMinutes</c> unless its worker renews it. The rows are not edited by hand, so a queue table is also a read-only list (<see cref="LogTablePlan"/>). </summary>
public static class QueuePlan
{
    public const string DefaultStatus = "Status";
    public const string DefaultRetry = "RetryCount";

    private static readonly string[] ProcessedStems = ["Processed", "Completed", "Finished", "Done"];
    private static readonly string[] ClaimedByNames = ["ClaimedBy", "LockedBy", "WorkerName", "Worker"];
    private static readonly string[] ErrorNames = ["LastError", "ErrorMessage", "Error", "Failure"];

    public static IReadOnlyList<(string Table, string Status, string Retry)> Entries(ProjectSettings project) =>
        project.QueueTables.Select(item =>
        {
            string[] parts = item.Split(':', StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : DefaultStatus, parts.Length > 2 && parts[2].Length > 0 ? parts[2] : DefaultRetry);
        }).Where(e => e.Item1.Length > 0).ToList();

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    public static QueueTable? For(TableModel table, ProjectSettings project) => IsListed(table.TableName, project) && Resolve(table, project, out _) is { } queue ? queue : null;

    /// <summary> Why a listed table cannot be a work queue, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table.TableName, project))
            return null;
        if (LogTablePlan.Problem(table, project) is { } listProblem)
            return listProblem.Replace("LogTables:", "QueueTables:");
        Resolve(table, project, out string? problem);
        return problem;
    }

    private static QueueTable? Resolve(TableModel table, ProjectSettings project, out string? problem)
    {
        problem = null;
        var entry = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName));
        string prefix = $"QueueTables: {table.TableName}";
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        ColumnModel? Named(string name) => table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
        var status = Named(entry.Status);
        var retry = Named(entry.Retry);
        var created = LogTablePlan.Guess(table);
        var claimed = table.Columns.FirstOrDefault(c => LogTablePlan.HasTimeOfDay(c) && c.Name.StartsWith("Claimed", StringComparison.OrdinalIgnoreCase) && !c.Name.EndsWith("By", StringComparison.OrdinalIgnoreCase));
        var processed = table.Columns.FirstOrDefault(c => LogTablePlan.HasTimeOfDay(c) && ProcessedStems.Any(s => c.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase)));
        var missing = new List<string>();
        if (status is not { IsStringColumn: true } || status.CharacterLength is > 0 and < 7)
            missing.Add($"{entry.Status} (text, at least 7 characters, holds pending, claimed, done or failed)");
        if (retry is not { IsIntegerColumn: true })
            missing.Add($"{entry.Retry} (whole number, the retries so far)");
        if (created is null)
            missing.Add("a created stamp (a date and time column)");
        if (claimed is null)
            missing.Add("ClaimedOn (a date and time column)");
        if (processed is null)
            missing.Add("ProcessedOn (a date and time column)");
        if (claimed is { IsNullable: false })
            missing.Add($"{claimed.Name} to allow NULL (a pending row is not claimed)");
        if (processed is { IsNullable: false })
            missing.Add($"{processed.Name} to allow NULL (a pending row is not processed)");
        if (missing.Count > 0)
        {
            problem = $"{prefix} needs {string.Join(", ", missing)}.";
            return null;
        }
        var claimedBy = table.Columns.FirstOrDefault(c => c.IsStringColumn && ClaimedByNames.Any(n => c.Name.EqualsIgnoreCase(n)));
        var lastError = table.Columns.FirstOrDefault(c => c.IsStringColumn && ErrorNames.Any(n => c.Name.EqualsIgnoreCase(n)));
        return new QueueTable(table, key, status!, retry!, created!, claimed!, processed!, claimedBy, lastError);
    }
}
