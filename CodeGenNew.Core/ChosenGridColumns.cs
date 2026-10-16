namespace CodeGenNew.Core;

/// <summary> One column a person may show in a grid: the generated grid shows it when <see cref="Initial"/>, and the person can show or hide it from the Columns list.
/// <see cref="Name"/> is the column's own name (what the grid sorts by and what the saved choice holds), <see cref="Property"/> the name in the JSON and in <c>$select</c>. </summary>
/// <param name="Sortable"> The server sorts by it (SearchSort): not long text or binary data. REST and OData grids sort by the same columns. </param>
/// <param name="Cut"> The cell shows the text cut to <see cref="ChosenGridColumns.CutLength"/> characters: a long text or a multi-line column. </param>
/// <param name="Filter"> The control of the OData filter row; null for a column that has none (long text, a value that is not text). </param>
public sealed record ChosenColumn(ColumnModel Column, string Name, string Property, string Label, string Caption, bool Initial, bool Sortable, bool Cut, ODataFilterField? Filter);

/// <summary> The one rule for which columns a user-chosen grid offers and starts with, and how a saved choice is read, so the Angular, React and Blazor grids (REST and OData) cannot drift apart.
/// A grid offers every column of the form in the form's order (<see cref="FormModel.Columns"/>), starts with the columns <see cref="GridColumns"/> picks, and never offers the key of a row whose key the
/// person does not see (it is always read). The person's choice is kept in the browser, per grid, as a list of column names. </summary>
public sealed class ChosenGridColumns
{
    /// <summary> A long or multi-line text shows this many characters in a grid cell (the rest is cut and ends in three dots). </summary>
    public const int CutLength = 60;

    private ChosenGridColumns(IReadOnlyList<ChosenColumn> offered) => Offered = offered;

    /// <summary> The columns the Columns list offers, in the form's order. </summary>
    public IReadOnlyList<ChosenColumn> Offered { get; }

    /// <summary> The columns the grid starts with (what <see cref="GridColumns"/> picks), in the form's order. </summary>
    public IReadOnlyList<ChosenColumn> Initial => Offered.Where(c => c.Initial).ToList();

    /// <summary> The columns whose header sorts the grid, whatever is shown (the shown ones sort). </summary>
    public IReadOnlyList<ChosenColumn> Sortable => Offered.Where(c => c.Sortable).ToList();

    public static ChosenGridColumns For(FormModel form)
    {
        var initial = form.GridColumns.ToHashSet();
        return new ChosenGridColumns(form.Columns
            .Select(c => new ChosenColumn(
                c, c.Name, JsonNames.Camel(c.Name), Labels.Field(c), GridCaption.For(c, Labels.Field(c)),
                initial.Contains(c), SearchSort.IsSortable(c), c.IsLongTextColumn || TemplateHelpers.IsMultiline(c), ODataFilterFields.Of(form, c)))
            .ToList());
    }

    /// <summary> What a grid shows given the saved choice: the saved names the table still offers, in the table's order; the starting columns when nothing valid was saved (no list, a value that is not
    /// a list of names, or names none of which is a column any more). </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string>? saved, IReadOnlyList<string> offered, IReadOnlyList<string> initial)
    {
        if (saved is null)
            return initial;
        var wanted = saved.ToHashSet();
        var kept = offered.Where(wanted.Contains).ToList();
        return kept.Count > 0 ? kept : initial;
    }

    /// <summary> The choice after the person ticks or clears one column: the table's order is kept, and the last shown column cannot be cleared (a grid with no column shows nothing to open a row from). </summary>
    public static IReadOnlyList<string> Toggle(IReadOnlyList<string> chosen, IReadOnlyList<string> offered, string name)
    {
        if (!offered.Contains(name))
            return chosen;
        if (chosen.Contains(name))
            return chosen.Count == 1 ? chosen : chosen.Where(n => n != name).ToList();
        var next = chosen.Append(name).ToHashSet();
        return offered.Where(next.Contains).ToList();
    }
}
