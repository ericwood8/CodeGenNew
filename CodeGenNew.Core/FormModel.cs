namespace CodeGenNew.Core;

/// <summary> What the form (and the editable part of the grid) of one table is made of, decided once for every front end: the columns a person fills in, the foreign key behind each column, which of
/// those foreign keys is a drop-down of the parent's rows, and the parents those drop-downs need. The React, Angular, Blazor and WinUI3 screens all read this instead of each working it out, so a rule
/// changed here changes every stack and the stacks cannot drift apart. </summary>
public sealed class FormModel
{
    private readonly ProjectSettings _project;
    private readonly IReadOnlyDictionary<string, ForeignKeyModel> _foreignKeyByColumn;

    private FormModel(TableModel table, ProjectSettings project)
    {
        Table = table;
        _project = project;
        KeyColumn = table.PrimaryKeyColumns.Count == 1 ? table.PrimaryKeyColumns[0] : null;
        KeyType = KeyColumn is null ? null : KeyType.Of(KeyColumn);
        TextKey = KeyType?.Kind == KeyKind.Text;

        _foreignKeyByColumn = table.ForeignKeyByColumn;

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
            .Select(fk => new FormParent(this, fk, project))
            .ToList();

        BaseName = ScreenNames.BaseName(table.TableName);
        Title = ScreenNames.Label(table.TableName);
        Variable = TemplateHelpers.LowerFirst(BaseName);
        Stem = ScreenNames.Stem(table.TableName);
        Pages = FormPages.For(Columns);
        GridColumns = Core.GridColumns.For(Columns);
        SortableColumns = GridColumns.Where(SearchSort.IsSortable).ToList();
        SearchFields = table.SearchableColumns.Select(c => new SearchField(c, c.Name, TemplateHelpers.LowerFirst(c.Name), Labels.Column(c.Name))).ToList();
        DateColumns = Columns.Where(c => TemplateHelpers.IsDate(c) && !c.IsSystemStampColumn).ToList();
        NewRowTypeScript = NewRowMembers(table, TextKey, KeyColumn, KeyType);
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

    /// <summary> The parent a column's drop-down reads, or null when the column is a plain box (see <see cref="LookupOf"/>). </summary>
    public FormParent? ParentOf(ColumnModel column) =>
        LookupOf(column) is { } foreignKey ? Parents.FirstOrDefault(p => p.Table.Equals(foreignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary> The foreign key behind a drop-down for this column, or null when the column is a plain box: the project says the parent is not looked up (<c>NoLookupParents</c>), the parent has no display
    /// column, or the column's type cannot be a key. A foreign key to the table itself (a tree) is a drop-down too. </summary>
    public ForeignKeyModel? LookupOf(ColumnModel column) =>
        KeyType.Of(column) is not null
        && _foreignKeyByColumn.TryGetValue(column.Name, out var fk)
        && !_project.NoLookupFor(fk.ReferencedTable, fk.ReferencedLookupShape)
        && fk.ReferencedDisplayColumns.Count > 0
            ? fk : null;

    /// <summary> The parents the drop-downs of <see cref="Columns"/> read, one per parent table in column order. </summary>
    public IReadOnlyList<FormParent> Parents { get; }

    /// <summary> The table's name without an E_ or SY_ prefix (<c>E_TimeSheet</c> is <c>TimeSheet</c>). </summary>
    public string BaseName { get; }

    /// <summary> What the screen calls one row: "Time Sheet". </summary>
    public string Title { get; }

    /// <summary> The title in lower case, for a sentence ("This time sheet no longer exists!"). </summary>
    public string Noun => Title.ToLowerInvariant();

    /// <summary> The base name with a lower-case first letter: <c>timeSheet</c>. </summary>
    public string Variable { get; }

    /// <summary> The base name in lower case, which the web stacks name the table's files from: <c>timesheet</c>. </summary>
    public string Stem { get; }

    /// <summary> The name of a form field's element: <c>timeSheetStartDate</c>. Every stack gives the same field the same id. </summary>
    public string FieldId(ColumnModel column) => Variable + TemplateHelpers.UpperFirst(JsonNames.Camel(column.Name));

    /// <summary> The table has a text column called Name, which the error messages of its screen can speak of. </summary>
    public bool HasName => Table.Columns.Any(c => c.IsStringColumn && c.Name == "Name");

    /// <summary> The table has a Name and an IsActive column, so its API is a name / active API with no plain list. </summary>
    public bool IsNameActive => Table.IsNameActiveTable;

    /// <summary> The grid has a Clone button: <see cref="CloneShape"/> allows the table (and a name / active table has none). </summary>
    public bool CanClone => !IsNameActive && CloneShape.CanClone(Table, _project);

    /// <summary> The tabs of the form (<see cref="FormPages"/>); one untitled page for a narrow table. </summary>
    public IReadOnlyList<FormPage> Pages { get; }

    public bool IsTabbed => Pages.Count > 1;

    /// <summary> The columns of the grid under the form (<see cref="Core.GridColumns.For"/>). </summary>
    public IReadOnlyList<ColumnModel> GridColumns { get; }

    /// <summary> The grid columns whose header sorts the grid. </summary>
    public IReadOnlyList<ColumnModel> SortableColumns { get; }

    /// <summary> One search box per searchable column, in the order of <see cref="TableModel.SearchableColumns"/>. </summary>
    public IReadOnlyList<SearchField> SearchFields { get; }

    /// <summary> The search boxes of a web screen: one per searchable column, or the one box of the Search list protocol (<c>ListProtocol=Search</c>), where the server decides which columns it matches. </summary>
    public IReadOnlyList<SearchField> WebSearchFields => _project.ListSearchProtocol ? [new SearchField(null, "search", "search", "")] : SearchFields;

    /// <summary> The date columns a person edits; a stamp the system writes keeps the timestamp the API sent. </summary>
    public IReadOnlyList<ColumnModel> DateColumns { get; }

    /// <summary> What a new row starts with, as members of a TypeScript object literal (<c>name: ''</c>, <c>quantity: 0</c>): a typed key empty, and every required column a starting value (a flag its database
    /// default, a date today, a number 0, anything else empty). Both web stacks write the same row. </summary>
    public IReadOnlyList<string> NewRowTypeScript { get; }

    private static List<string> NewRowMembers(TableModel table, bool textKey, ColumnModel? keyColumn, KeyType? keyType)
    {
        var members = new List<string>();
        if (textKey)
            members.Add($"{JsonNames.Camel(keyColumn!.Name)}: {keyType!.TypeScriptEmpty}");
        foreach (var c in table.Columns.Where(c => !c.IsPrimaryKey && !c.IsNullable && !c.IsComputed && !c.IsIdentity))
        {
            string value = c.IsBooleanColumn ? (c.DatabaseDefaultSql is not null && c.DatabaseDefaultSql.Trim('(', ')', ' ') == "1" ? "true" : "false")
                : TemplateHelpers.IsDate(c) ? "new Date().toISOString().substring(0, 10)"
                : TemplateHelpers.IsNumber(c) ? "0"
                : "''";
            members.Add($"{JsonNames.Camel(c.Name)}: {value}");
        }
        return members;
    }

    /// <summary> The foreign key points at the table itself. </summary>
    public bool IsSelfReference(ForeignKeyModel foreignKey) => foreignKey.ReferencedTable.Equals(Table.TableName, StringComparison.OrdinalIgnoreCase);

    /// <summary> The project leaves a foreign key to this table off the screen (<c>HiddenParents</c>: a styling id no one edits). </summary>
    public bool IsHiddenParent(string table) => _project.HiddenParents is { } hidden && hidden.Contains(table, StringComparer.OrdinalIgnoreCase);

    /// <summary> How the grid shows a column of this table (<see cref="GridCells.KindOf"/>). </summary>
    public CellKind CellOf(ColumnModel column) => GridCells.KindOf(column, LookupOf(column) is not null);

    /// <summary> Which editor a column gets on the form. The order is the rule every stack follows: the first that fits wins. </summary>
    public FieldKind KindOf(ColumnModel column) =>
        column.IsSystemStampColumn ? FieldKind.SystemStamp
        : TextKey && column.IsPrimaryKey ? FieldKind.TypedKey
        : LookupOf(column) is not null ? FieldKind.Lookup
        : column.HasChoices ? FieldKind.Choices
        : column.IsBooleanColumn ? FieldKind.Boolean
        : TemplateHelpers.IsDate(column) ? FieldKind.Date
        : IsMoneyInput(column) ? FieldKind.Money
        : TemplateHelpers.IsNumber(column) ? FieldKind.Number
        : TemplateHelpers.IsMultiline(column) ? FieldKind.Multiline
        : FieldKind.Text;

    /// <summary> How an editor that keeps numbers as numbers and everything else as text (the WinUI3 view model) holds a column's value. The first that fits wins, so every screen of that kind reads, loads and saves a
    /// column the same way. A system stamp is held as what it is (a date stays a date); only <see cref="KindOf"/> decides that it is shown as text. </summary>
    public ValueKind ValueOf(ColumnModel column) =>
        LookupOf(column) is not null ? ValueKind.Lookup
        : IsMoneyInput(column) ? ValueKind.Money
        : IsDecimalBox(column) ? ValueKind.Decimal
        : IsWholeNumberBox(column) ? ValueKind.WholeNumber
        : column.IsBooleanColumn ? ValueKind.Boolean
        : TemplateHelpers.IsDate(column) ? ValueKind.Date
        : column.IsGuidColumn ? ValueKind.Guid
        : TemplateHelpers.IsNumber(column) ? ValueKind.Number
        : ValueKind.Text;

    /// <summary> A decimal, float or real column that is not money (a percentage, a rate, a weight): edited in a number box limited to what the column can hold. </summary>
    public static bool IsDecimalBox(ColumnModel column) => !column.IsCurrencyColumn && column.IsNumericColumn;

    /// <summary> A whole-number column (not a flag): edited in a number box limited to its range. </summary>
    public static bool IsWholeNumberBox(ColumnModel column) => column.IsIntegerColumn && !column.IsBooleanColumn;

    /// <summary> A money column that is edited in a money box: a foreign key, a list of choices, a stamp and the typed key are not. </summary>
    public bool IsMoneyInput(ColumnModel column) =>
        column.IsMoneyAmount && TemplateHelpers.IsNumber(column) && !column.IsSystemStampColumn && LookupOf(column) is null && !column.HasChoices && !(TextKey && column.IsPrimaryKey);
}

/// <summary> One search box: the column it searches, the name the API takes it by and the caption. </summary>
/// <param name="Column"> The searchable column; null for the single box of the Search list protocol. </param>
/// <param name="Name"> The column's name. </param>
/// <param name="ParamName"> The query parameter and filter field: the column name with a lower-case first letter. </param>
/// <param name="Label"> The caption of the box. </param>
public sealed record SearchField(ColumnModel? Column, string Name, string ParamName, string Label);

/// <summary> The editor a form gives a column (see <see cref="FormModel.KindOf"/>). </summary>
public enum FieldKind
{
    /// <summary> Written by the system (who made the row and when): shown as text once the row exists. </summary>
    SystemStamp,
    /// <summary> The key a person types on a new row; read-only afterwards. </summary>
    TypedKey,
    /// <summary> A foreign key: a drop-down of the parent's rows. </summary>
    Lookup,
    /// <summary> A column whose values the database lists: a drop-down of them. </summary>
    Choices,
    Boolean,
    Date,
    /// <summary> An amount of money: a money box. </summary>
    Money,
    /// <summary> A whole or decimal number that is not money. </summary>
    Number,
    /// <summary> Text that needs several lines. </summary>
    Multiline,
    Text
}

/// <summary> How a view model holds a column's value (see <see cref="FormModel.ValueOf"/>). </summary>
public enum ValueKind
{
    /// <summary> A foreign key to a parent that is looked up: the parent's key, or null for none. </summary>
    Lookup,
    /// <summary> An amount of money, held as a double (NaN is blank) and saved as a rounded decimal. </summary>
    Money,
    /// <summary> A decimal, float or real that is not money, held as a double (NaN is blank). </summary>
    Decimal,
    /// <summary> A whole number, held as a double (NaN is blank) and saved after checking it is whole and in range. </summary>
    WholeNumber,
    Boolean,
    Date,
    /// <summary> A uniqueidentifier typed as text. </summary>
    Guid,
    /// <summary> Any other number, typed as text. </summary>
    Number,
    Text
}
