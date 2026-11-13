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
        TypedKey = KeyType.IsTyped(table, project);

        _foreignKeyByColumn = table.ForeignKeyByColumn;

        // A typed key is the first field of a new row. Otherwise: not the key, not computed or identity, not a password hash, a type a box can edit, and not a foreign key to a parent the project hides.
        // A column called Name comes first, so the field a person looks for is at the top.
        Columns = table.Columns
            .Where(c => (TypedKey && c.IsPrimaryKey)
                        || (!c.IsPrimaryKey && !c.IsComputed && !c.IsIdentity && !c.IsCredentialColumn && TemplateHelpers.IsEditableType(c)
                            && !(_foreignKeyByColumn.TryGetValue(c.Name, out var hidden) && IsHiddenParent(hidden.ReferencedTable))))
            .OrderBy(c => TypedKey && c.IsPrimaryKey ? 0 : c.Name == "Name" ? 1 : 2).ThenBy(c => c.OrdinalPosition)
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
        GridColumns = Core.GridColumns.For(Columns, project.MaxGridColumns);
        SortableColumns = GridColumns.Where(SearchSort.IsSortable).ToList();
        SearchKeys = _project.ListSearchProtocol ? [] : table.SearchKeyColumns
            .Select(c => (Column: c, Parent: ParentOf(c)))
            .Where(k => k.Parent is not null)
            .Select(k => new SearchKeyField(k.Column, k.Parent!, TemplateHelpers.LowerFirst(k.Column.Name), GridCaption.For(k.Column, k.Column.Label)))
            .ToList();
        // a text key the screen has no drop-down for is a search box again: it sends the key's own parameter, which matches exactly
        TextKeyBoxes = table.SearchKeyColumns.Where(c => c.IsStringColumn && SearchKeys.All(k => k.Column != c)).ToList();
        SearchFields = table.SearchableColumns.Concat(TextKeyBoxes).OrderBy(c => c.OrdinalPosition)
            .Select(c => new SearchField(c, c.Name, TemplateHelpers.LowerFirst(c.Name), GridCaption.For(c, c.Label))).ToList();
        Log = TableShapes.For(table, project).Log;
        DateRange = Log is null ? [] : [new SearchField(Log.Timestamp, "From", LogShape.FromParameter, "From", true), new SearchField(Log.Timestamp, "To", LogShape.ToParameter, "To", true)];
        DateColumns = Columns.Where(c => TemplateHelpers.IsDate(c) && !c.IsSystemStampColumn && !IsGuardStamp(c) && !ContentScreens.IsStateColumn(table, project, c) && !SnapshotPlan.IsOwnedColumn(table, project, c)).ToList();
        NewRowTypeScript = NewRowMembers(table, TypedKey, KeyColumn, KeyType);
    }

    /// <summary> The model of <paramref name="table"/> for the project's settings. </summary>
    public static FormModel For(TableModel table, ProjectSettings project) => new(table, project);

    public TableModel Table { get; }

    /// <summary> The single primary key column, or null for no key or a composite key. </summary>
    public ColumnModel? KeyColumn { get; }

    /// <summary> How the key travels (a number, a uniqueidentifier or text); null when there is no usable single key. </summary>
    public KeyType? KeyType { get; }

    /// <summary> The key is a text key. </summary>
    public bool TextKey { get; }

    /// <summary> The key is typed by the person on a new row and read-only afterwards (<see cref="Core.KeyType.IsTyped"/>: a text key, or a whole number the database does not hand out), so it is a field of the form and a column of the grid. </summary>
    public bool TypedKey { get; }

    /// <summary> The columns of the form, in the order they appear. </summary>
    public IReadOnlyList<ColumnModel> Columns { get; }

    /// <summary> The flag that hides a field of the form (<c>FeatureFields</c>), or null: the field is in the form only while the flag is on for the signed-in person. </summary>
    public string? FeatureOf(ColumnModel column) => FeatureScreens.Reads(_project) ? FeatureScreens.FieldFlagOf(_project, Table.TableName, column.Name) : null;

    /// <summary> The flags that hide a field of the form, each once in the order of the fields; the screen keeps one value per flag (<see cref="FeatureIndex"/> numbers them). </summary>
    public IReadOnlyList<string> FeatureFlags => Columns.Select(FeatureOf).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

    /// <summary> The place of a flag in <see cref="FeatureFlags"/>, for the name of the variable that holds its value. </summary>
    public int FeatureIndex(string flag) => FeatureFlags.ToList().IndexOf(flag);

    /// <summary> The parent a column's drop-down reads, or null when the column is a plain box (see <see cref="LookupOf"/>). </summary>
    public FormParent? ParentOf(ColumnModel column) =>
        LookupOf(column) is { } foreignKey ? Parents.FirstOrDefault(p => p.Table.Equals(foreignKey.ReferencedTable, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary> The foreign key behind a drop-down for this column, or null when the column is a plain box: the project says the parent is not looked up (<c>NoLookupParents</c>), the parent has no display
    /// column, or the column's type cannot be a key. A foreign key to the table itself (a tree) is a drop-down too. </summary>
    public ForeignKeyModel? LookupOf(ColumnModel column) =>
        KeyType.Of(column) is not null
        && _foreignKeyByColumn.TryGetValue(column.Name, out var fk)
        && !_project.NoLookupFor(fk.ReferencedTable, fk.ReferencedLookupShape)
        && fk.ReferencesPrimaryKey
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

    /// <summary> The words of one row in lower case ("time sheet"), the same as <see cref="Noun"/>. </summary>
    public string Words => Noun;

    /// <summary> The words of several rows in lower case ("time sheets"). </summary>
    public string PluralWords => Noun.Pluralize();

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

    /// <summary> One search box per searchable column, in table order, and one for each text key without a drop-down (<see cref="TextKeyBoxes"/>). </summary>
    public IReadOnlyList<SearchField> SearchFields { get; }

    /// <summary> The text foreign keys (<see cref="TableModel.SearchKeyColumns"/>) the screen has no drop-down for, because the parent is not read or the Search list protocol has one box. </summary>
    public IReadOnlyList<ColumnModel> TextKeyBoxes { get; }

    /// <summary> The search boxes of a web screen: one per searchable column, or the one box of the Search list protocol (<c>ListProtocol=Search</c>), where the server decides which columns it matches. </summary>
    public IReadOnlyList<SearchField> WebSearchFields => _project.ListSearchProtocol ? [new SearchField(null, "search", "search", "")] : [.. SearchFields, .. DateRange];

    /// <summary> The table the project lists in LogTables (a read-only, newest-first list with a date range), or null. </summary>
    public LogShape? Log { get; }

    /// <summary> True for a log table: no Add, Edit, Clone or Delete and no form. </summary>
    public bool ReadOnly => Log is not null;

    /// <summary> A log table's first and last day boxes (empty for any other table): the search fields the API takes as <c>from</c> and <c>to</c>, last in <see cref="WebSearchFields"/>. </summary>
    public IReadOnlyList<SearchField> DateRange { get; }

    /// <summary> One drop-down per whole-number foreign key the search can filter by (<see cref="TableModel.SearchKeyColumns"/>) whose parent the screen already reads for its form: picking a parent's row
    /// sends its key, an exact match. Empty with the Search list protocol (the one box is the whole search). </summary>
    public IReadOnlyList<SearchKeyField> SearchKeys { get; }

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

    /// <summary> The distinct parent tables the given columns show the names of (a grid's foreign key columns), in column order. </summary>
    public List<FormParent> ParentsOf(IEnumerable<ColumnModel> columns) =>
        columns.Select(ParentOf).OfType<FormParent>().DistinctBy(p => p.Table, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> Long text is a multi-line box, except an email address, a phone number or a URL: those are one line, whatever their column length, so the browser can check them. </summary>
    public bool IsMultilineBox(ColumnModel column) => TemplateHelpers.IsMultiline(column) && column.InputType(_project, Table.TableName) == "text";

    /// <summary> The column as this form shows it: it answers which editor it gets and how a view model holds its value (see <see cref="FormColumn"/>). </summary>
    public FormColumn Field(ColumnModel column)
    {
        if (!_fields.TryGetValue(column, out var field))
            _fields[column] = field = new FormColumn(this, _project, column);
        return field;
    }

    private readonly Dictionary<ColumnModel, FormColumn> _fields = [];

    /// <summary> The project's guarded columns of this table (GuardedColumns): the screen asks the API which of them the caller may change and shows the others as text with the sentence. </summary>
    public IReadOnlyList<GuardHint> Guards => _guards ??= GatePlan.GuardsOf(_project, Table);
    private IReadOnlyList<GuardHint>? _guards;

    /// <summary> The guard of this column, or null when it is not guarded. </summary>
    public GuardHint? GuardOf(ColumnModel column) => Guards.FirstOrDefault(g => g.Column.EqualsIgnoreCase(column.Name));

    /// <summary> A column the server stamps for a guard (who changed the guarded column, and when): shown as text, never edited. </summary>
    public bool IsGuardStamp(ColumnModel column) => Guards.Any(g => g.Stamps.Any(s => s.EqualsIgnoreCase(column.Name)));

    /// <summary> The person must give this column a value (see <see cref="FormColumn.IsRequired"/>). </summary>
    public bool IsRequired(ColumnModel column) => Field(column).IsRequired;

    /// <summary> The caption of this column carries the red asterisk (see <see cref="FormColumn.IsMarked"/>). </summary>
    public bool IsMarked(ColumnModel column) => Field(column).IsMarked;

    /// <summary> A money column that is edited in a money box (see <see cref="FormColumn.IsMoneyInput"/>). </summary>
    public bool IsMoneyInput(ColumnModel column) => Field(column).IsMoneyInput;
}

/// <summary> One search box: the column it searches, the name the API takes it by and the caption. </summary>
/// <param name="Column"> The searchable column; null for the single box of the Search list protocol. </param>
/// <param name="Name"> The column's name. </param>
/// <param name="ParamName"> The query parameter and filter field: the column name with a lower-case first letter. </param>
/// <param name="Label"> The caption of the box. </param>
public sealed record SearchField(ColumnModel? Column, string Name, string ParamName, string Label, bool IsDate = false);

/// <summary> One key filter: the foreign key column, the parent whose rows the drop-down lists, the query parameter and filter field (the column name with a lower-case first letter) and the caption,
/// the same words as the column's grid heading. </summary>
public sealed record SearchKeyField(ColumnModel Column, FormParent Parent, string ParamName, string Label);
