namespace CodeGenNew.Core;

/// <summary> Which of a table's columns a generated grid shows: at most <see cref="DefaultMaxColumns"/> columns (the project's <c>MaxGridColumns</c> changes the number) that are not long text, in the order given.
/// Long-text columns (ColumnModel.IsLongTextColumn: notes, memos, remarks, long varchars) never appear in a grid, however few columns there are; the
/// edit form shows them on its Notes tab instead. </summary>
public static class GridColumns
{
    /// <summary> The most columns a grid shows when the project says nothing. </summary>
    public const int DefaultMaxColumns = 18;

    /// <summary> A grid with fewer columns than this is filled up with the table's multi-line text columns (100 characters or more), so a table whose text is all long still shows what each row is. </summary>
    public const int MinColumns = 3;

    /// <summary> The columns a grid starts from, before <see cref="ForGrid"/>: those that are not multi-line text, and, when that leaves fewer than <see cref="MinColumns"/>, the first multi-line ones that are not long text
    /// (in the order given) up to that many; a table with no other column shows them all. </summary>
    public static List<ColumnModel> Choose(IReadOnlyList<ColumnModel> shown, Func<ColumnModel, bool> isMultiline)
    {
        var plain = shown.Where(c => !isMultiline(c)).ToList();
        if (plain.Count == 0)
            return shown.ToList();
        int visible = plain.Count(c => !c.IsLongTextColumn);   // ForGrid drops the long ones afterwards
        if (visible >= MinColumns)
            return plain;
        var fill = shown.Where(c => isMultiline(c) && !c.IsLongTextColumn).Take(MinColumns - visible).ToHashSet();
        return shown.Where(c => plain.Contains(c) || fill.Contains(c)).ToList();
    }

    /// <summary> The columns a generated grid shows for these form columns: <see cref="Choose"/> with the shared rule for multi-line text, then <see cref="ForGrid"/>. Every stack asks this one question. </summary>
    public static List<ColumnModel> For(IReadOnlyList<ColumnModel> columns, int max = DefaultMaxColumns) => Choose(columns, TemplateHelpers.IsMultiline).ForGrid(max);

    /// <summary> The columns for a grid. A table whose columns are ALL long text would otherwise get an empty grid, so then (and only then) its long-text columns are used. </summary>
    public static List<ColumnModel> ForGrid(this IEnumerable<ColumnModel> columns, int max = DefaultMaxColumns)
    {
        var all = columns.ToList();
        var ordinary = all.Where(c => !c.IsLongTextColumn).Take(max).ToList();
        return ordinary.Count > 0 ? ordinary : all.Take(max).ToList();
    }
}
