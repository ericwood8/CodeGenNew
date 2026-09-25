namespace CodeGenNew.Core;

/// <summary> Decides which columns of a table are its "critical display columns" -- what a Lookup shows a person
/// so they can recognize a row (its description/name/number), as opposed to its ID. </summary>
public static class DisplayColumnSelector
{
    /// <summary> Columns whose name matches SpecialLogicColumns.config's DisplayColumn rule (ColumnModel.DisplayRank
    /// set), in table order. Primary-key and foreign-key columns are never display columns: they ARE the IDs a Lookup
    /// returns anyway, and a key column that happens to match a pattern (RegionCode against "*Code") must not hide
    /// the real description next to it. If nothing matches, falls back to the first ordinary string column so a
    /// table always contributes something recognizable when it has any text; if it has none, returns empty. </summary>
    /// <param name="foreignKeyColumns"> Names of this table's columns that take part in a foreign key. </param>
    public static List<ColumnModel> SelectDisplayColumns(this IReadOnlyList<ColumnModel> columns, IReadOnlyCollection<string> foreignKeyColumns)
    {
        bool IsKey(ColumnModel c) =>
            c.IsPrimaryKey || foreignKeyColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase);

        var chosen = columns.Where(c => c.IsDisplayColumn && !IsKey(c)).ToList();
        if (chosen.Count > 0)
            return chosen;

        var fallback = columns.FirstOrDefault(c =>
            c.IsStringColumn && !IsKey(c) && !c.IsAuditColumn
            && c.SqlType is not (System.Data.SqlDbType.Text or System.Data.SqlDbType.NText));
        return fallback is null ? [] : [fallback];
    }
}
