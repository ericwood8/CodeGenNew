namespace CodeGenNew.Core;

/// <summary> Two date columns of one table where the second must not be earlier than the first (a start and its end): the form refuses an end before its start, the end's picker starts at the start, and the validators
/// of every stack say the same sentence. A date may equal the other, and a blank one is never judged. </summary>
public sealed record DatePair(ColumnModel Start, ColumnModel End)
{
    /// <summary> The sentence every stack answers with. </summary>
    public string Message => RuleMessages.NotBefore(Labels.Words(End.Name), Labels.Words(Start.Name));
}

/// <summary> The pairs of a table: the start and end <c>SpecialLogicColumns.config</c> detects (<c>StartEndDate</c>, the pair SP_Insert and SP_Update already refuse in the routines), the pairs the project names in
/// <c>DatePairs</c> (<c>Start>End</c>, or <c>Table.Start>End</c> for one table), and the CHECK constraints that say a date is not later than another. The project's <c>NoDatePairs</c> (a table, an end column, or
/// <c>Table.End</c>) takes a pair away. A column is the end of at most one pair. </summary>
public static class DatePairs
{
    public static IReadOnlyList<DatePair> For(TableModel table, ProjectSettings project)
    {
        var pairs = new List<DatePair>();
        bool Ok(ColumnModel c) => TemplateHelpers.IsDate(c) && !c.IsComputed && !c.IsPrimaryKey;
        void Add(ColumnModel? start, ColumnModel? end)
        {
            if (start is null || end is null || ReferenceEquals(start, end) || start.Name == end.Name || !Ok(start) || !Ok(end))
                return;
            if (pairs.Any(p => p.End.Name == end.Name) || Exempt(table, end, project))
                return;
            pairs.Add(new DatePair(start, end));
        }

        if (table.HasStartEndDatePair)
            Add(table.StartDateColumn, table.EndDateColumn);

        foreach (string entry in project.DatePairs)
        {
            var (scope, names) = Split(entry);
            if (scope is not null && !scope.EqualsIgnoreCase(table.TableName))
                continue;
            Add(table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(names.Start)), table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(names.End)));
        }

        foreach (var end in table.Columns.Where(c => c.NotBeforeColumn is not null))
            Add(table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(end.NotBeforeColumn!)), end);
        return pairs;
    }

    /// <summary> The pair whose end is this column, or null. </summary>
    public static DatePair? EndingAt(TableModel table, ProjectSettings project, ColumnModel column) => For(table, project).FirstOrDefault(p => p.End.Name == column.Name);

    /// <summary> The pair whose start is this column, or null (the first when it starts several). </summary>
    public static DatePair? StartingAt(TableModel table, ProjectSettings project, ColumnModel column) => For(table, project).FirstOrDefault(p => p.Start.Name == column.Name);

    private static bool Exempt(TableModel table, ColumnModel end, ProjectSettings project) => project.NoDatePairs
        .Any(entry => entry.EqualsIgnoreCase(table.TableName) || entry.EqualsIgnoreCase(end.Name) || entry.EqualsIgnoreCase($"{table.TableName}.{end.Name}"));

    private static (string? Scope, (string Start, string End) Names) Split(string entry)
    {
        int arrow = entry.IndexOf('>');
        if (arrow < 0)
            return (null, ("", ""));
        string left = entry[..arrow].Trim(), right = entry[(arrow + 1)..].Trim();
        int dot = left.IndexOf('.');
        return dot < 0 ? (null, (left, right)) : (left[..dot], (left[(dot + 1)..], right));
    }
}
