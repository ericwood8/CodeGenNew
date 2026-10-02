namespace CodeGenNew.Core;

/// <summary> Which of a table's columns a generated grid shows: at most <see cref="MaxColumns"/> columns that are not long text, in the order given.
/// Long-text columns (ColumnModel.IsLongTextColumn: notes, memos, remarks, long varchars) never appear in a grid, however few columns there are; the
/// edit form shows them on its Notes tab instead. </summary>
public static class GridColumns
{
    public const int MaxColumns = 18;

    /// <summary> The columns for a grid. A table whose columns are ALL long text would otherwise get an empty grid, so then (and only then) its long-text columns are used. </summary>
    public static List<ColumnModel> ForGrid(this IEnumerable<ColumnModel> columns)
    {
        var all = columns.ToList();
        var ordinary = all.Where(c => !c.IsLongTextColumn).Take(MaxColumns).ToList();
        return ordinary.Count > 0 ? ordinary : all.Take(MaxColumns).ToList();
    }
}
