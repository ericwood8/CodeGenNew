namespace CodeGenNew.Core;

/// <summary> A foreign key of a child grid shown as the parent's name: the child column, the parent table and its key and display columns, the C# type of the id the child row holds, whether the parent has too
/// many rows to read whole (<see cref="TypeAhead"/>: only the names of the keys the rows show are read), whether the parent has a lookup endpoint (<see cref="Lookup"/>), the name of the dictionary that holds the
/// names and the header ("CustomerId" is "Customer"). </summary>
public sealed record DesktopChildLookup(string Column, string ReferencedTable, string KeyProp, string DisplayProp, string KeyCs, bool TypeAhead, bool Lookup, string LookupVar, string Label);

/// <summary> A drop-down above a child grid that filters its rows by the name one of its foreign keys shows. </summary>
public sealed record DesktopChildFilter(string Column, string Label, string All, string Choices, string Prop);

/// <summary> One child grid of a desktop master-detail dialog (WinUI3 and Uno): the child table and its model class, the grid's variable, the link back to the row, what the child's columns show and how, and the
/// names it resolves. </summary>
public sealed record DesktopChild(
    string Table, string Type, string? KeyProp, bool HasChildren, string Title, string Var, string FkColumn, string ParentKeyColumn, string ParentKeyCs,
    IReadOnlyList<string> HideColumns, IReadOnlyList<DesktopChildLookup> Resolvable, IReadOnlyList<DesktopChildFilter> Filters,
    IReadOnlyList<(string Name, int Digits)> Currency, IReadOnlyList<string> LongText, IReadOnlyList<(string Name, string Caption)> Captions)
{
    /// <summary> The child grids of a master-detail dialog of <paramref name="table"/>: the <see cref="ChildGrid"/>s whose link column is a usable key (the grid's filter compares the parent's key).
    /// <paramref name="needsOwnKey"/>: also only children with a single-column key that are not name / active tables (a front end that reads and deletes the rows through the API needs them). </summary>
    public static List<DesktopChild> For(TableModel table, ProjectSettings project, bool needsOwnKey)
    {
        return ChildGrid.For(table, project)
            .Where(g => !needsOwnKey || (g.KeyName is not null && !g.IsNameActive))
            .Where(g => table.Columns.Any(c => c.Name.EqualsIgnoreCase(g.Link.ReferencedColumns[0]) && KeyType.Of(c) is not null))
            .Select(g =>
            {
                var fk = g.Link;
                string gridVar = TemplateHelpers.LowerFirst(g.Table + g.Role);
                var resolvable = g.Lookups.Select(l => new DesktopChildLookup(
                    l.ColumnName, l.ParentTable, l.ParentKeyName, l.ParentDisplayName,
                    l.ColumnKeyType?.CSharp ?? "int", l.TypeAhead, l.UsesLookupEndpoint, gridVar + l.ParentTable + "Names",
                    GridCaption.From(Labels.Column(l.ColumnName)))).ToList();
                // a drop-down per foreign key shown as a name: the names the rows have, picked from here; the grid holds every row of the child, so it filters them itself
                var filters = resolvable.Take(ChildGrid.MaxFilters)
                    .Select(r => new DesktopChildFilter(r.Column, r.Label, "All " + r.Label.Pluralize(), gridVar + r.Column + "Choices", TemplateHelpers.UpperFirst(gridVar) + r.Column + "Filter")).ToList();

                return new DesktopChild(
                    fk.ReferencingTable, BlazorNames.TypeName(fk.ReferencingTable), g.KeyName, fk.ReferencingTableHasChildren, g.Title, gridVar,
                    fk.ReferencingColumns[0], fk.ReferencedColumns[0],
                    table.Columns.First(c => c.Name.EqualsIgnoreCase(fk.ReferencedColumns[0])).CSharpBase(),   // int, Guid or string: the type the grid's filter compares
                    g.HiddenColumns.ToList(), resolvable, filters,
                    // What the schema says about the child's columns: money shows as currency, long text goes last.
                    fk.ReferencingTableColumns.Where(c => c.IsMoneyAmount).Select(c => (c.Name, NumericClassifier.CurrencyDigits(c))).ToList(),
                    fk.ReferencingTableColumns.Where(c => c.IsLongTextColumn).Select(c => c.Name).ToList(),
                    // The caption each child column gets (date columns lose "Date", yes/no columns end in "?", Number -> #, Percent -> %).
                    fk.ReferencingTableColumns.Select(c => (c.Name, GridCaption.For(c, Labels.Column(c.Name)))).ToList());
            }).ToList();
    }
}
