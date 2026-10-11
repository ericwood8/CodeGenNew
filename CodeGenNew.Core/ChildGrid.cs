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

    /// <summary> The JSON name of the child column that holds the key. </summary>
    public string ColumnJson => JsonNames.Camel(ColumnName);

    /// <summary> The api route a web client reads the parent's names from: the lookup list for a whole-number key, else the parent's rows (<c>customers/lookup</c>, <c>customers</c>). </summary>
    public string WebRoute => ScreenNames.ApiRoute(ParentTable) + (UsesLookupEndpoint ? "/lookup" : "");

    /// <summary> The JSON name of the parent's key in what <see cref="WebRoute"/> answers. </summary>
    public string WebKey => UsesLookupEndpoint ? "id" : JsonNames.Camel(ParentKeyName);

    /// <summary> The JSON name of the parent's display column in what <see cref="WebRoute"/> answers. </summary>
    public string WebDisplay => UsesLookupEndpoint ? "name" : JsonNames.Camel(ParentDisplayName);
}

/// <summary> One read-only (or, with <c>EditableChildren</c>, editable) grid of a child table under a master's form, decided once for every front end: which child tables get a grid, what the grid
/// is called, which of the child's columns hold the link back to the row, its own key, and the names it resolves. A child that references the parent twice (a donation has a donor and a
/// recipient) gets one grid per foreign key, told apart by the <see cref="Role"/> the column plays. </summary>
public sealed class ChildGrid
{
    private readonly ProjectSettings _project;

    private ChildGrid(ChildForeignKeyModel link, string role, string title, TableModel parent, ProjectSettings project)
    {
        _project = project;
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

    /// <summary> <see cref="Name"/> with a lower-case first letter: the grid's variable on the web screens. </summary>
    public string Variable => TemplateHelpers.LowerFirst(Name);

    /// <summary> The property of the save request that holds this child's rows when the table is edited in the form (<c>EditableChildren</c>). </summary>
    public string JsonName => EditableChild.JsonName(Link);

    /// <summary> The route of the child's api: <c>salesinvoices</c>. </summary>
    public string ApiRoute => ScreenNames.ApiRoute(Table);

    /// <summary> The route of the child's own screen: <c>sales-invoice</c>. A row of the grid opens it. </summary>
    public string PageRoute => ScreenNames.Route(Table);

    /// <summary> The JSON name of the child's key; null for a composite key. </summary>
    public string? KeyJson => KeyName is null ? null : JsonNames.Camel(KeyName);

    /// <summary> A text key travels encoded in the links and the delete route. </summary>
    public bool KeyIsText => KeyType?.Kind == KeyKind.Text;

    /// <summary> The JSON name of the child column that points back at the row being edited. </summary>
    public string ForeignKeyJson => JsonNames.Camel(Link.ReferencingColumns[0]);

    /// <summary> <see cref="HiddenColumns"/> by their JSON names. </summary>
    public IReadOnlyList<string> HiddenJson => HiddenColumns.Select(JsonNames.Camel).ToList();

    /// <summary> The child's amounts of money (JSON name and decimal places): the grid shows them as currency. </summary>
    public IReadOnlyList<(string Name, int Digits)> MoneyColumns => Link.ReferencingTableColumns.Where(c => c.IsMoneyAmount).Select(c => (JsonNames.Camel(c.Name), NumericClassifier.CurrencyDigits(c))).ToList();

    /// <summary> The child's long text columns by their JSON names: the grid puts them last. </summary>
    public IReadOnlyList<string> LongTextJson => Link.ReferencingTableColumns.Where(c => c.IsLongTextColumn).Select(c => JsonNames.Camel(c.Name)).ToList();

    /// <summary> The caption each child column gets in the grid (JSON name and caption: date columns lose "Date", yes/no columns end in "?", Number becomes #, Percent becomes %). </summary>
    public IReadOnlyList<(string Name, string Caption)> Captions => Link.ReferencingTableColumns.Select(c => (JsonNames.Camel(c.Name), GridCaption.For(c, Labels.Column(c.Name)))).ToList();

    /// <summary> The foreign key behind a drop-down for one of the child's columns when its rows are edited inside the form: a column that is not a uniqueidentifier, points at a parent with a display
    /// column and an API, and that the project does not leave unlooked-up. Null for a plain box. </summary>
    public ForeignKeyModel? EditLookupOf(ColumnModel column) => column.IsGuidColumn ? null
        : Link.ReferencingTableForeignKeys.FirstOrDefault(o => o.ReferencingColumns.Count == 1 && o.ReferencingColumns[0].Equals(column.Name, StringComparison.OrdinalIgnoreCase)
            && o.ReferencedDisplayColumns.Count > 0 && !_project.NoApiFor(o.ReferencedTable, o.ReferencedLookupShape) && !_project.NoLookupFor(o.ReferencedTable, o.ReferencedLookupShape));

    /// <summary> A money column of the child that is edited in a money box: not a foreign key drop-down or a list of choices. </summary>
    public bool IsMoneyInput(ColumnModel column) => column.IsMoneyAmount && TemplateHelpers.IsNumber(column) && EditLookupOf(column) is null && !column.HasChoices;

    /// <summary> The columns of the child a person edits inside the form (<see cref="EditableChild.Columns"/>). </summary>
    public IReadOnlyList<ColumnModel> EditColumns => EditableChild.Columns(Link);

    /// <summary> The money box of one of the child's columns. </summary>
    public MoneyBox MoneyBoxOf(ColumnModel column) => MoneyBox.For(Table, column, _project);

    /// <summary> The number box of one of the child's columns. </summary>
    public NumberBox NumberBoxOf(ColumnModel column) => NumberBox.For(Table, column, _project);

    /// <summary> The caption of an editable child column carries the red asterisk: a NOT NULL column that is not a yes/no box (the same rule as <see cref="FormModel.IsMarked"/>). </summary>
    public bool IsMarked(ColumnModel column) => _project.RequiredMarks && !column.IsNullable && !column.IsBooleanColumn;

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
