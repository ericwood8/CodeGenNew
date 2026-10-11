namespace CodeGenNew.Core;

/// <summary> A foreign key of a child grid that is shown as the parent's name (a customer's purchase history lists item numbers, not item ids). </summary>
/// <param name="ForeignKey"> The child table's foreign key. </param>
/// <param name="Column"> The child column that holds the parent's key. </param>
/// <param name="ColumnKeyType"> How that key travels (a number, a uniqueidentifier or text). </param>
public sealed record ChildLookup(ForeignKeyModel ForeignKey, ColumnModel? Column, KeyType? ColumnKeyType)
{
    public string ColumnName => ForeignKey.ReferencingColumns[0];
    public string ParentTable => ForeignKey.ReferencedTable;
    public string ParentKeyName => ForeignKey.ReferencedColumns[0];
    public string ParentDisplayName => ForeignKey.ReferencedDisplayColumns[0];

    /// <summary> A whole-number key is named by the parent's <c>GET &lt;route&gt;/lookup</c> (id and name only); any other key reads the parent's rows. </summary>
    public bool UsesLookupEndpoint => ColumnKeyType?.Kind == KeyKind.Int;
}

/// <summary> One read-only (or, with <c>EditableChildren</c>, editable) grid of a child table under a master's form, decided once for every front end: which child tables get a grid, what the grid
/// is called, which of the child's columns hold the link back to the row, its own key, and the names it resolves. A child that references the parent twice (a donation has a donor and a
/// recipient) gets one grid per foreign key, told apart by the <see cref="Role"/> the column plays. </summary>
public sealed class ChildGrid
{
    private ChildGrid(ChildForeignKeyModel link, string role, string title, TableModel parent, ProjectSettings project)
    {
        Link = link;
        Role = role;
        Title = title;
        BaseName = TemplateHelpers.BaseName(link.ReferencingTable);
        ForeignKeyColumn = link.ReferencingTableColumns.FirstOrDefault(c => c.Name.Equals(link.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase));
        KeyColumn = link.ReferencingPrimaryKeyColumns.Count == 1
            ? link.ReferencingTableColumns.FirstOrDefault(c => c.Name.Equals(link.ReferencingPrimaryKeyColumns[0], StringComparison.OrdinalIgnoreCase))
            : null;
        KeyType = KeyColumn is null ? null : KeyType.Of(KeyColumn);

        // The child's other foreign keys that point at a table with a display column are shown as that name, unless the project says that parent is not looked up (as in the table's own grid).
        Lookups = link.ReferencingTableForeignKeys
            .Where(o => o.ReferencingColumns.Count == 1 && !o.ReferencingColumns[0].Equals(link.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase)
                        && !o.ReferencedTable.Equals(link.ReferencingTable, StringComparison.OrdinalIgnoreCase)
                        && o.ReferencedDisplayColumns.Count > 0 && !project.NoApiFor(o.ReferencedTable, default) && !project.NoLookupFor(o.ReferencedTable, o.ReferencedLookupShape))
            .GroupBy(o => o.ReferencingColumns[0], StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(o => new ChildLookup(o, link.ReferencingTableColumns.FirstOrDefault(c => c.Name.Equals(o.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase)), KeyType.OfForeignKey(link.ReferencingTableColumns, o)))
            .ToList();

        // The grid's own foreign key (always this row), the child's own key and every other id column that is not resolved to a name are left out.
        var resolved = Lookups.Select(l => l.ColumnName).ToList();
        HiddenColumns = link.ReferencingColumns.Concat(link.ReferencingPrimaryKeyColumns)
            .Concat(link.ReferencingTableForeignKeys.SelectMany(o => o.ReferencingColumns).Except(resolved, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        IsEditable = EditableChild.IsEditable(parent, link, project);
    }

    /// <summary> The child tables of <paramref name="table"/> that get a grid: a single-column foreign key from a table the project gives an API. </summary>
    public static IReadOnlyList<ChildGrid> For(TableModel table, ProjectSettings project)
    {
        var links = table.ChildForeignKeys
            .Where(fk => fk.ReferencingColumns.Count == 1 && !project.NoApiFor(fk.ReferencingTable, default))
            .ToList();

        string RoleOf(ChildForeignKeyModel fk) => links.Count(o => o.ReferencingTable.Equals(fk.ReferencingTable, StringComparison.OrdinalIgnoreCase)) > 1 ? RoleName(fk.ReferencingColumns[0]) : "";

        return links.Select(fk =>
        {
            string role = RoleOf(fk);
            string title = project.ChildGridTitle(table.TableName, fk.ReferencingTable, Labels.Words(TemplateHelpers.BaseName(fk.ReferencingTable)) + (role == "" ? "" : " (" + Labels.Words(role) + ")"));
            return new ChildGrid(fk, role, title, table, project);
        }).ToList();
    }

    // "Donor_EmployeeId" -> "DonorEmployee": the role the foreign key's column plays.
    private static string RoleName(string column) => System.Text.RegularExpressions.Regex.Replace(column, "Id$", "").Replace("_", "");

    public ChildForeignKeyModel Link { get; }

    public string Table => Link.ReferencingTable;

    /// <summary> The table's name without an E_ or SY_ prefix. </summary>
    public string BaseName { get; }

    /// <summary> What tells this grid from another grid of the same child table; empty when the child references the parent once. </summary>
    public string Role { get; }

    /// <summary> The name of the grid's variable and methods: the base name plus the role. </summary>
    public string Name => BaseName + Role;

    /// <summary> The heading of the grid: the project's <c>ChildGridTitles</c> entry, else the table's name (with the role when there are several). </summary>
    public string Title { get; }

    /// <summary> The child column that points back at the row being edited. </summary>
    public ColumnModel? ForeignKeyColumn { get; }

    /// <summary> The name of the child's own single-column key; null for a composite key (the grid cannot then edit or delete a row). </summary>
    public string? KeyName => Link.ReferencingPrimaryKeyColumns.Count == 1 ? Link.ReferencingPrimaryKeyColumns[0] : null;

    /// <summary> The child's key column, when the model carries the child's columns. </summary>
    public ColumnModel? KeyColumn { get; }

    public KeyType? KeyType { get; }

    /// <summary> The child is a name / active table, whose API is hand-maintained and has no plain list. </summary>
    public bool IsNameActive => TableModel.HasNameActiveShape(Link.ReferencingTableColumns);

    /// <summary> The other foreign keys that are resolved to the parent's name. </summary>
    public IReadOnlyList<ChildLookup> Lookups { get; }

    /// <summary> The child's column names the grid leaves out. </summary>
    public IReadOnlyList<string> HiddenColumns { get; }

    /// <summary> The project lists the table in <c>EditableChildren</c>: its rows are edited inside the parent's form and saved with it. </summary>
    public bool IsEditable { get; }
}
