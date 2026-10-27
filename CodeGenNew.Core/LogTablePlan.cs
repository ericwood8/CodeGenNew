using System.Data;

namespace CodeGenNew.Core;

/// <summary> A table the application only reads: an insert-only ledger (errors, sessions, data changes) shown newest first with a date range, no Add, Edit, Clone or Delete and no write routes. </summary>
public sealed record LogTable(TableModel Table, ColumnModel Timestamp)
{
    /// <summary> The query parameters of the date range: the first and the last day (inclusive) of the rows to list. </summary>
    public const string FromParameter = "from";
    public const string ToParameter = "to";
}

/// <summary> Which tables are log tables and which column is their timestamp. The project lists them (<c>LogTables=ErrorLog,UserSession:StartedOn</c>, a table and optionally its timestamp column);
/// a table that looks like one but is not listed is only mentioned in the run notes, since making a table read-only is not something to do on a guess. </summary>
public static class LogTablePlan
{
    private static readonly string[] TimestampNames = ["Logged", "Occurred", "Timestamp", "Created", "Started", "Time", "Stamp", "Date", "At", "On"];
    private static readonly string[] LogWords = ["Log", "Audit", "Session", "Event", "History", "Trace", "Activity", "Error"];
    private static readonly string[] LevelNames = ["Level", "Severity"];

    /// <summary> What the setting names: a table and the timestamp column it chose (null: the plan picks one). </summary>
    public static IReadOnlyList<(string Table, string? Column)> Entries(ProjectSettings project) =>
        project.LogTables.Select(item =>
        {
            int colon = item.IndexOf(':');
            return colon < 0 ? (item.Trim(), (string?)null) : (item[..colon].Trim(), (string?)item[(colon + 1)..].Trim());
        }).Where(e => e.Item1.Length > 0).ToList();

    public static bool IsListed(TableModel table, ProjectSettings project) => IsListed(table.TableName, project);

    public static bool IsListed(string table, ProjectSettings project) => Entries(project).Any(e => e.Table.EqualsIgnoreCase(table));

    /// <summary> The log table the project lists, or null (not listed, or a problem: see <see cref="Problem"/>). </summary>
    public static LogTable? For(TableModel table, ProjectSettings project) =>
        Problem(table, project) is null && IsListed(table, project) && TimestampOf(table, project) is { } stamp ? new LogTable(table, stamp) : null;

    /// <summary> Why a listed table cannot be a log table, or null when it can (or is not listed). </summary>
    public static string? Problem(TableModel table, ProjectSettings project)
    {
        if (!IsListed(table, project))
            return null;
        if (!table.HasCrudApi(project))
            return $"LogTables: {table.TableName} gets no API (an enum, a lookup table or a table the NoApiTables setting lists), so there is nothing to show.";
        if (project.AccessModeFor(table.Dialect) != AccessMode.Ef)
            return $"LogTables: {table.TableName} is read through the EF search, so the project needs AccessMode=Ef (a routines project keeps its own search).";
        if (project.ListSearchProtocol)
            return $"LogTables: {table.TableName} needs the Columns list protocol (its date range is two more query parameters); ListProtocol=Search has one box.";
        if (project.OData && ODataPlan.Serves(table, project))
            return $"LogTables: {table.TableName} is served as OData, whose routes also write rows; take it out of ODataTables.";
        return TimestampOf(table, project) is null
            ? $"LogTables: {table.TableName} has no date and time column to list newest first; name one (LogTables={table.TableName}:ColumnName)."
            : null;
    }

    private static ColumnModel? TimestampOf(TableModel table, ProjectSettings project)
    {
        var named = Entries(project).First(e => e.Table.EqualsIgnoreCase(table.TableName)).Column;
        if (named is not null)
            return table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(named) && c.IsDateColumn);
        return Guess(table);
    }

    /// <summary> The column a log is ordered by when the setting names none: a date-and-time column, the one whose name says when something happened first. </summary>
    public static ColumnModel? Guess(TableModel table)
    {
        var stamps = table.Columns.Where(c => HasTimeOfDay(c) && !c.IsModifiedDateColumn && !c.IsLastChangedDateColumn).ToList();
        if (stamps.Count <= 1)
            return stamps.FirstOrDefault();
        return stamps.OrderBy(Rank).ThenBy(c => c.OrdinalPosition).First();
    }

    private static int Rank(ColumnModel column)
    {
        int index = Array.FindIndex(TimestampNames, w => column.Name.ContainsIgnoreCase(w));
        return index < 0 ? int.MaxValue : index;
    }

    public static bool HasTimeOfDay(ColumnModel column) =>
        column.SqlType is SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime;

    /// <summary> Whether a table has the shape of a log: an integer key, a date-and-time column and either a log-like name or a level and a message. Only a run note uses it. </summary>
    public static bool Looks(TableModel table)
    {
        if (table.PrimaryKeyColumns is not [{ IsIntegerColumn: true }] || Guess(table) is null || table.Columns.Any(c => c.IsModifiedDateColumn || c.IsModifiedUserColumn))
            return false;
        bool named = LogWords.Any(w => table.TableName.ContainsIgnoreCase(w));
        bool levelAndMessage = table.Columns.Any(c => LevelNames.Any(l => c.Name.EqualsIgnoreCase(l))) && table.Columns.Any(c => c.Name.ContainsIgnoreCase("Message"));
        return named || levelAndMessage;
    }
}
