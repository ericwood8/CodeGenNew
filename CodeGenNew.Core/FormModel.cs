namespace CodeGenNew.Core;

/// <summary> What the form (and the editable part of the grid) of one table is made of, decided once for every front end: the columns a person fills in, the foreign key behind each column, which of
/// those foreign keys is a drop-down of the parent's rows, and the parents those drop-downs need. The React, Angular, Blazor and WinUI3 screens all read this instead of each working it out, so a rule
/// changed here changes every stack and the stacks cannot drift apart. </summary>
public sealed class FormModel
{
    private readonly ProjectSettings _project;
    private readonly Dictionary<string, ForeignKeyModel> _foreignKeyByColumn;

    private FormModel(TableModel table, ProjectSettings project)
    {
        Table = table;
        _project = project;
        KeyColumn = table.PrimaryKeyColumns.Count == 1 ? table.PrimaryKeyColumns[0] : null;
        KeyType = KeyColumn is null ? null : KeyType.Of(KeyColumn);
        TextKey = KeyType?.Kind == KeyKind.Text;

        // GroupBy + First, not ToDictionary: a real table can have more than one constraint on the same column pointing at the same parent.
        _foreignKeyByColumn = table.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1)
            .GroupBy(f => f.ReferencingColumns[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // A typed key is the first field of a new row. Otherwise: not the key, not computed or identity, not a password hash, a type a box can edit, and not a foreign key to a parent the project hides.
        // A column called Name comes first, so the field a person looks for is at the top.
        Columns = table.Columns
            .Where(c => (TextKey && c.IsPrimaryKey)
                        || (!c.IsPrimaryKey && !c.IsComputed && !c.IsIdentity && !c.IsCredentialColumn && TemplateHelpers.IsEditableType(c)
                            && !(_foreignKeyByColumn.TryGetValue(c.Name, out var hidden) && IsHiddenParent(hidden.ReferencedTable))))
            .OrderBy(c => TextKey && c.IsPrimaryKey ? 0 : c.Name == "Name" ? 1 : 2).ThenBy(c => c.OrdinalPosition)
            .ToList();

        Parents = Columns.Select(LookupOf).OfType<ForeignKeyModel>()
            .GroupBy(fk => fk.ReferencedTable, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .ToList();
    }

    /// <summary> The model of <paramref name="table"/> for the project's settings. </summary>
    public static FormModel For(TableModel table, ProjectSettings project) => new(table, project);

    public TableModel Table { get; }

    /// <summary> The single primary key column, or null for no key or a composite key. </summary>
    public ColumnModel? KeyColumn { get; }

    /// <summary> How the key travels (a number, a uniqueidentifier or text); null when there is no usable single key. </summary>
    public KeyType? KeyType { get; }

    /// <summary> The key is typed by the person on a new row and read-only afterwards, so it is a field of the form and a column of the grid. </summary>
    public bool TextKey { get; }

    /// <summary> The columns of the form, in the order they appear. </summary>
    public IReadOnlyList<ColumnModel> Columns { get; }

    /// <summary> The foreign key a column carries (a single-column key), whether or not it is a drop-down. </summary>
    public ForeignKeyModel? ForeignKeyOf(ColumnModel column) => _foreignKeyByColumn.GetValueOrDefault(column.Name);

    /// <summary> Every single-column foreign key of the table, by the name of its column. </summary>
    public IReadOnlyDictionary<string, ForeignKeyModel> ForeignKeysByColumn => _foreignKeyByColumn;

    /// <summary> The foreign key behind a drop-down for this column, or null when the column is a plain box: the project says the parent is not looked up (<c>NoLookupParents</c>), the parent has no display
    /// column, or the column's type cannot be a key. A foreign key to the table itself (a tree) is a drop-down too. </summary>
    public ForeignKeyModel? LookupOf(ColumnModel column) =>
        KeyType.Of(column) is not null
        && _foreignKeyByColumn.TryGetValue(column.Name, out var fk)
        && !_project.NoLookupFor(fk.ReferencedTable, fk.ReferencedLookupShape)
        && fk.ReferencedDisplayColumns.Count > 0
            ? fk : null;

    /// <summary> The parents the drop-downs of <see cref="Columns"/> read, one foreign key per parent table in column order. </summary>
    public IReadOnlyList<ForeignKeyModel> Parents { get; }

    /// <summary> The foreign key points at the table itself. </summary>
    public bool IsSelfReference(ForeignKeyModel foreignKey) => foreignKey.ReferencedTable.Equals(Table.TableName, StringComparison.OrdinalIgnoreCase);

    /// <summary> The project leaves a foreign key to this table off the screen (<c>HiddenParents</c>: a styling id no one edits). </summary>
    public bool IsHiddenParent(string table) => _project.HiddenParents is { } hidden && hidden.Contains(table, StringComparer.OrdinalIgnoreCase);

    /// <summary> A money column that is edited in a money box: a foreign key, a list of choices, a stamp and the typed key are not. </summary>
    public bool IsMoneyInput(ColumnModel column) =>
        column.IsMoneyAmount && TemplateHelpers.IsNumber(column) && !column.IsSystemStampColumn && LookupOf(column) is null && !column.HasChoices && !(TextKey && column.IsPrimaryKey);
}
