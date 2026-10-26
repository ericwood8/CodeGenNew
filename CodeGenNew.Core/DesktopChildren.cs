namespace CodeGenNew.Core;

/// <summary> A foreign key of a child grid shown as the parent's name: the child column, the parent table and its key and display columns, the C# type of the id the child row holds, whether the parent has too
/// many rows to read whole (<see cref="TypeAhead"/>: only the names of the keys the rows show are read), whether the parent has a lookup endpoint (<see cref="Lookup"/>), the name of the dictionary that holds the
/// names and the header ("CustomerId" is "Customer"). </summary>
public sealed record DesktopChildLookup(string Column, string ReferencedTable, string KeyProp, string DisplayProp, string KeyCs, bool TypeAhead, bool Lookup, string LookupVar, string Label);

/// <summary> A drop-down above a child grid that filters its rows by the name one of its foreign keys shows. </summary>
public sealed record DesktopChildFilter(string Column, string Label, string All, string Choices, string Prop);

/// <summary> One child grid of a desktop master-detail dialog (WinUI3 and Uno): the child table and its model class, the grid's variable, the link back to the row, what the child's columns show and how, and the
/// names it resolves. <see cref="Member"/> is the child table plus the role of the link (two grids of one table, a bill of materials' assembly and component, differ by it) and names the grid's methods and handlers. <see cref="HasDialog"/> is false for a child that has no screen (a many-to-many junction table has a composite key, so the plan writes no dialog for it): its rows are listed and can be deleted,
/// but there is no Edit button, and a click on a row opens nothing. </summary>
public sealed record DesktopChild(
    string Member, string Table, string Type, string? KeyProp, bool HasChildren, bool HasDialog, string Title, string Var, string FkColumn, string ParentKeyColumn, string ParentKeyCs,
    IReadOnlyList<string> HideColumns, IReadOnlyList<DesktopChildLookup> Resolvable, IReadOnlyList<DesktopChildFilter> Filters,
    IReadOnlyList<(string Name, int Digits)> Currency, IReadOnlyList<string> LongText, IReadOnlyList<(string Name, string Caption)> Captions, ChildGrid Grid, bool Editable)
{
    // the child's own dialog exists when the table is a screen: listed in the project's Screens, or (with none listed) a plain table with a single-column key
    // two foreign keys of one child to the same parent (a component and an assembly, both products) need a names dictionary each
    private static bool SharesParent(ChildGrid grid, ChildLookup lookup) => grid.Lookups.Count(o => o.ParentTable == lookup.ParentTable) > 1;

    private static bool HasScreen(ChildGrid grid, ProjectSettings project) =>
        project.Screens.Length > 0
            ? project.Screens.Contains(grid.Table, StringComparer.OrdinalIgnoreCase)
            : grid.KeyName is not null && !grid.IsNameActive;

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
                    l.ColumnKeyType?.CSharp ?? "int", l.TypeAhead, l.UsesLookupEndpoint, gridVar + (SharesParent(g, l) ? l.ColumnName : l.ParentTable) + "Names",
                    GridCaption.From(Labels.Column(l.ColumnName)))).ToList();
                // a drop-down per foreign key shown as a name: the names the rows have, picked from here; the grid holds every row of the child, so it filters them itself
                var filters = resolvable.Take(ChildGrid.MaxFilters)
                    .Select(r => new DesktopChildFilter(r.Column, r.Label, "All " + r.Label.Pluralize(), gridVar + r.Column + "Choices", TemplateHelpers.UpperFirst(gridVar) + r.Column + "Filter")).ToList();

                return new DesktopChild(
                    g.Table + g.Role, fk.ReferencingTable, BlazorNames.TypeName(fk.ReferencingTable), g.KeyName, fk.ReferencingTableHasChildren,
                    HasScreen(g, project), g.Title, gridVar,
                    fk.ReferencingColumns[0], fk.ReferencedColumns[0],
                    table.Columns.First(c => c.Name.EqualsIgnoreCase(fk.ReferencedColumns[0])).CSharpBase(),   // int, Guid or string: the type the grid's filter compares
                    g.HiddenColumns.ToList(), resolvable, filters,
                    // What the schema says about the child's columns: money shows as currency, long text goes last.
                    fk.ReferencingTableColumns.Where(c => c.IsMoneyAmount).Select(c => (c.Name, NumericClassifier.CurrencyDigits(c))).ToList(),
                    fk.ReferencingTableColumns.Where(c => c.IsLongTextColumn).Select(c => c.Name).ToList(),
                    // The caption each child column gets (date columns lose "Date", yes/no columns end in "?", Number -> #, Percent -> %).
                    fk.ReferencingTableColumns.Select(c => (c.Name, GridCaption.For(c, Labels.Column(c.Name)))).ToList(),
                    g, DesktopEditRows.Applies(g, table));
            }).ToList();
    }
}
