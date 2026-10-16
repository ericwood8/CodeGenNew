using System.Data;

namespace CodeGenNew.Core;

/// <summary> One column of a Rust struct: its names, type, the attributes serde and sqlx need, and the pieces of SQL that read and write it. </summary>
public sealed record RustField(ColumnModel Column, string Name, string Json, string Type, bool IsOption, string? SerdeWith, string Select)
{
    /// <summary> The field's type without <c>Option</c>. </summary>
    public string BaseType => IsOption ? Type[7..^1] : Type;
}

/// <summary> A navigation property the .NET entity has (and so the JSON of the other stacks: always null in a list): the Rust struct carries it as an empty <c>Option</c> so the JSON has the same names. </summary>
public sealed record RustNavigation(string Name, string Json);

/// <summary> A filter of the search: the Rust field of the request, the query-string name, and the SQL text before and after the bound value. </summary>
public sealed record RustFilter(string Field, string Query, string Before, string After);

/// <summary> One whole-number foreign key the search can filter by: an exact match, the quoted column as the SQL names it. </summary>
public sealed record RustKeyFilter(string Field, string Query, string Column, string Type)
{
    public bool IsText => Type == "String";
}

/// <summary> A sort the grid may ask for: the name in lower case and the SQL expression to order by. </summary>
public sealed record RustSort(string Name, string Expression);

/// <summary> The clone of a row: one INSERT ... SELECT. The columns in <see cref="Overrides"/> take a bound value (a free one the repository suggests), in the order of the placeholders. </summary>
public sealed record RustClone(string Sql, IReadOnlyList<RustField> Overrides);

/// <summary> What a Rust API needs to know about one table, worked out once from the schema: the struct, and every statement of the repository written for the database (PostgreSQL, MySQL
/// or SQLite) as fixed text. The templates only lay this out. A column whose type Rust has no mapping for here (binary, xml, a type CodeGenNew cannot map) is left out of the struct and the
/// statements and is listed in <see cref="Skipped"/>. </summary>
public sealed class RustTable
{
    private readonly DialectInfo _di;
    private readonly Dictionary<ColumnModel, RustField> _fieldOf;

    private RustTable(TableModel model, ProjectSettings project)
    {
        Model = model;
        _di = DialectInfo.For(model.Dialect);
        Struct = RustNames.Pascal(model.TableName);
        Module = RustNames.Snake(model.TableName);
        Route = "/api/" + ScreenNames.BaseName(model.TableName).ToLowerInvariant().Pluralize();

        var used = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<RustField>();
        var skipped = new List<ColumnModel>();
        foreach (var column in model.Columns)
        {
            string? type = RustType(column, model.Dialect);
            if (type is null)
            {
                skipped.Add(column);
                continue;
            }

            string snake = RustNames.Snake(column.Name);
            for (int n = 2; !used.Add(snake); n++)
                snake = RustNames.Snake(column.Name) + "_" + n;
            fields.Add(new RustField(column, RustNames.Ident(snake), JsonNames.Camel(column.Name), column.IsNullable ? $"Option<{type}>" : type, column.IsNullable,
                SerdeWith(type, column.IsNullable), SelectExpression(column)));
        }

        Fields = fields;
        Skipped = skipped;
        Key = fields.First(f => f.Column.IsPrimaryKey);
        // the user table's password hash is read into the struct (the sign-in needs it) but is in no JSON, no INSERT, no UPDATE, no filter, no sort and no clone: only set-password writes it
        string? hashName = UserTable.PlanOf(model, project)?.PasswordHash.Name;
        HashField = hashName is null ? null : fields.FirstOrDefault(f => f.Column.Name.EqualsIgnoreCase(hashName));
        var fieldOf = new Dictionary<ColumnModel, RustField>();
        foreach (var field in fields.Where(f => f != HashField))
            fieldOf.TryAdd(field.Column, field);
        _fieldOf = fieldOf;
        Navigations = EntityNavigations.Of(model, fk => project.NoNavigation(fk.ReferencedTable, fk.ReferencedLookupShape) ?? false)
            .Where(n => fieldOf.ContainsKey(n.Column))
            .Select(n => new RustNavigation(RustNames.Ident(RustNames.Snake(n.Role)), JsonNames.Camel(n.Role))).ToList();
        Returning = model.Dialect != SqlDialect.MySql;
        Table = _di.QuoteTable(model.SchemaName, model.DbTableName);
        SelectList = string.Join(", ", fields.Select(f => f.Select));

        // what an INSERT writes: everything the database does not assign (an identity or computed column is left out)
        Written = fields.Where(f => f != HashField && !f.Column.IsIdentity && !f.Column.IsComputed).ToList();
        string columns = string.Join(", ", Written.Select(f => _di.Quote(f.Column.DbName)));
        string values = string.Join(", ", Written.Select((f, i) => Parameter(f, i + 1)));
        string returning = Returning ? $" RETURNING {SelectList}" : "";
        string key = _di.Quote(Key.Column.DbName);

        SelectAll = $"SELECT {SelectList} FROM {Table} ORDER BY {key}";
        SelectOne = $"SELECT {SelectList} FROM {Table} WHERE {key} = {_di.Placeholder(1)}";
        Insert = $"INSERT INTO {Table} ({columns}) VALUES ({values}){returning}";

        // an UPDATE sets every written column except the key, then finds the row by the key (last, so the positional placeholders of MySQL and SQLite follow the bind order)
        Updated = Written.Where(f => !f.Column.IsPrimaryKey).ToList();
        string sets = string.Join(", ", Updated.Select((f, i) => $"{_di.Quote(f.Column.DbName)} = {Parameter(f, i + 1)}"));
        Update = $"UPDATE {Table} SET {sets} WHERE {key} = {_di.Placeholder(Updated.Count + 1)}{returning}";
        Delete = $"DELETE FROM {Table} WHERE {key} = {_di.Placeholder(1)}";

        BuildSearch(project);
        BuildLookup();
        Clone = CloneShape.CanClone(model, project) ? BuildClone() : null;
    }

    public static RustTable Of(TableModel model, ProjectSettings project) => new(model, project);

    /// <summary> SQL as a Rust raw string literal (<c>r#"..."#</c>), so the quotes of an identifier need no escape. </summary>
    public static string Raw(string text) => text.Contains("\"#", StringComparison.Ordinal) ? "r##\"" + text + "\"##" : "r#\"" + text + "\"#";

    public TableModel Model { get; }

    /// <summary> The struct's name: <c>SalesInvoice</c>. </summary>
    public string Struct { get; }

    /// <summary> The module and file name: <c>sales_invoice</c>. </summary>
    public string Module { get; }

    /// <summary> The route every stack uses for the table: <c>/api/customers</c> (the plural of <see cref="Pluralizer"/>). </summary>
    public string Route { get; }

    public IReadOnlyList<RustField> Fields { get; }
    public IReadOnlyList<ColumnModel> Skipped { get; }
    public RustField Key { get; }

    /// <summary> The password hash field of the user table (<c>UserTable</c> with <c>ApiAuth</c>); null for any other table. </summary>
    public RustField? HashField { get; }

    /// <summary> The navigation properties of the entity, which a Rust row carries only as null. </summary>
    public IReadOnlyList<RustNavigation> Navigations { get; }

    /// <summary> False for MySQL, which cannot return the row an INSERT or UPDATE wrote: the repository reads it back by key. </summary>
    public bool Returning { get; }

    public string Table { get; }
    public string SelectList { get; }

    /// <summary> The fields an INSERT binds, in order. </summary>
    public IReadOnlyList<RustField> Written { get; }

    /// <summary> The fields an UPDATE binds, in order, before the key. </summary>
    public IReadOnlyList<RustField> Updated { get; }

    public string SelectAll { get; private set; } = "";
    public string SelectOne { get; private set; } = "";
    public string Insert { get; private set; } = "";
    public string Update { get; private set; } = "";
    public string Delete { get; private set; } = "";

    /// <summary> The search statement before the filters: <c>SELECT ... FROM t AS t WHERE 1 = 1</c>. </summary>
    public string SearchItems { get; private set; } = "";
    public string SearchCount { get; private set; } = "";
    public IReadOnlyList<RustFilter> Filters { get; private set; } = [];

    /// <summary> The key filters (<see cref="SearchPlan.KeyFilters"/>): an optional integer query parameter each, compared with the column as written. </summary>
    public IReadOnlyList<RustKeyFilter> KeyFilters { get; private set; } = [];

    // the field's type without the Option a nullable column adds
    private static string BareType(RustField field) => field.IsOption && field.Type.StartsWith("Option<") ? field.Type["Option<".Length..^1] : field.Type;
    public IReadOnlyList<RustSort> Sorts { get; private set; } = [];

    /// <summary> The order that applies without a sort and follows a chosen one: the best display column, then the key. </summary>
    public string DefaultOrder { get; private set; } = "";

    /// <summary> The statement behind <c>GET &lt;route&gt;/lookup</c>, the list the drop-downs and the "name of" columns of other screens read: <c>id</c>, <c>name</c> (the display column as text, blank when null)
    /// and <c>is_active</c> (the IsActive flag, null when the table has none) of every row, ordered by name. Null when the table has no display column. </summary>
    public string? LookupSql { get; private set; }

    /// <summary> Null when the table gets no Clone button (<see cref="CloneShape.CanClone"/>). </summary>
    public RustClone? Clone { get; }

    /// <summary> The Rust type of a column, or null when there is none (the column is left out). </summary>
    public static string? RustType(ColumnModel column, SqlDialect dialect)
    {
        string declaration = column.SqlTypeDeclaration.ToLowerInvariant();
        switch (column.SqlType)
        {
            case SqlDbType.Int when declaration.Contains("unsigned"):
                return "u32";
            case SqlDbType.Int:
                return "i32";
            case SqlDbType.BigInt when declaration.Contains("unsigned"):
                return "u64";
            case SqlDbType.BigInt:
                return "i64";
            case SqlDbType.SmallInt when declaration.Contains("unsigned"):
                return "u16";
            case SqlDbType.SmallInt when declaration.StartsWith("tinyint", StringComparison.Ordinal):
                return "i8";
            case SqlDbType.SmallInt when declaration == "year":
                return null;
            case SqlDbType.SmallInt:
                return "i16";
            case SqlDbType.TinyInt:
                return "u8";
            case SqlDbType.Bit:
                return "bool";
            case SqlDbType.Decimal when declaration.Contains("unsigned"):
                return "u64";
            case SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney:
                return dialect == SqlDialect.Sqlite ? "f64" : "Decimal";
            case SqlDbType.Float:
                return "f64";
            case SqlDbType.Real:
                return "f32";
            case SqlDbType.Date:
                return "NaiveDate";
            case SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime:
                return "NaiveDateTime";
            case SqlDbType.DateTimeOffset:
                return "DateTime<Utc>";
            case SqlDbType.Time:
                return "NaiveTime";
            case SqlDbType.UniqueIdentifier:
                return "Uuid";
            case SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText:
                return "String";
            default:
                return null;
        }
    }

    // The module of src/support.rs (or the serde feature of a crate) that writes and reads the type the way ASP.NET does.
    private static string? SerdeWith(string type, bool option) => type switch
    {
        "Decimal" => option ? "rust_decimal::serde::float_option" : "rust_decimal::serde::float",
        "NaiveDate" => option ? "crate::support::json_date_option" : "crate::support::json_date",
        "NaiveDateTime" => option ? "crate::support::json_date_time_option" : "crate::support::json_date_time",
        _ => null
    };

    private string SelectExpression(ColumnModel column)
    {
        string q = _di.Quote(column.DbName);
        if (_di.Dialect == SqlDialect.PostgreSql)
        {
            if (column.IsMoneyColumn)
                return $"{q}::numeric AS {q}";
            if (column.DbEnumType is not null)
                return $"{q}::text AS {q}";
        }
        // SQLite stores a whole number that was written to a DECIMAL or FLOAT column as an integer, and sqlx refuses to read an integer as a double: read the value as the real it means
        if (_di.Dialect == SqlDialect.Sqlite && (column.IsNumericColumn || column.IsMoneyColumn))
            return $"CAST({q} AS REAL) AS {q}";
        return q;
    }

    // The placeholder of a bound value, with the cast PostgreSQL needs for the two types sqlx sends as something else: money (sent as numeric) and an enum (sent as text).
    private string Parameter(RustField field, int index)
    {
        string p = _di.Placeholder(index);
        if (_di.Dialect != SqlDialect.PostgreSql)
            return p;
        if (field.Column.IsMoneyColumn)
            return $"{p}::numeric::money";
        if (field.Column.DbEnumType is { } type)
            return $"CAST({p}::text AS {string.Join(".", type.Split('.').Select(_di.Quote))})";
        return p;
    }

    private void BuildSearch(ProjectSettings project)
    {
        var plan = SearchPlan.For(Model, []);
        string from = $"{Table} AS t";
        SearchItems = $"SELECT {SelectList} FROM {from} WHERE 1 = 1";
        SearchCount = $"SELECT COUNT(*) FROM {from} WHERE 1 = 1";

        string TextOf(ColumnModel c) => "t." + _di.Quote(c.DbName) + (_di.Dialect == SqlDialect.PostgreSql && c.DbEnumType is not null ? "::text" : "");

        Filters = plan.Filters.Where(f => _fieldOf.ContainsKey(f.Column)).Select(f =>
        {
            var (before, after) = _di.ContainsParts(TextOf(f.Column));
            string field = _fieldOf[f.Column].Name;
            return new RustFilter(field, f.ParameterName, before, after);
        }).ToList();

        KeyFilters = plan.KeyFilters.Where(k => _fieldOf.ContainsKey(k.Column))
            .Select(k => new RustKeyFilter(_fieldOf[k.Column].Name, k.ParameterName, "t." + _di.Quote(k.Column.DbName), k.Column.IsIntegerColumn ? "i64" : BareType(_fieldOf[k.Column]))).ToList();

        Sorts = SearchSort.Entries(Model)
            .Where(e => _fieldOf.ContainsKey(e.Column))
            .Select(e =>
            {
                string expression = e.Parent is { } fk
                    ? $"(SELECT p.{_di.Quote(fk.ReferencedDisplayDbColumns[0])} FROM {_di.QuoteTable(fk.ReferencedSchema, fk.ReferencedDbTable)} AS p WHERE p.{_di.Quote(fk.ReferencedDbColumns[0])} = t.{_di.Quote(e.Column.DbName)} LIMIT 1)"
                    : TextOf(e.Column);
                return new RustSort(e.Name.ToLowerInvariant(), expression);
            }).ToList();

        DefaultOrder = string.Join(", ", plan.DefaultOrder.Select(c => "t." + _di.Quote(c.DbName)));
    }

    private void BuildLookup()
    {
        var display = Model.DisplayColumns.FirstOrDefault(c => _fieldOf.ContainsKey(c));
        if (display is null)
            return;
        string name = _di.Quote(display.DbName);
        if (_di.Dialect == SqlDialect.PostgreSql && display.DbEnumType is not null)
            name += "::text";
        else if (!display.IsStringColumn)
            name = _di.ToText(name);
        if (display.IsNullable)
            name = $"COALESCE({name}, '')";
        var active = Model.Columns.FirstOrDefault(c => c.IsBooleanColumn && !c.IsNullable && c.Name == "IsActive" && _fieldOf.ContainsKey(c));
        string flag = active is null ? "NULL" : _di.Quote(active.DbName);
        LookupSql = $"SELECT {_di.Quote(Key.Column.DbName)} AS id, {name} AS name, {flag} AS is_active FROM {Table} ORDER BY {name}";
    }

    private RustClone BuildClone()
    {
        var m = Model;
        var overrides = CloneShape.OverrideColumns(m);
        var active = m.HasActiveInactivePair ? m.ActiveColumn : null;
        var inactiveDate = m.HasActiveInactivePair ? m.InactiveDateColumn : null;
        bool activeNegative = active is not null && active.IsInactive;
        var columns = new List<string>();
        var values = new List<string>();
        var bound = new List<RustField>();

        foreach (var column in CloneShape.Copyable(m))
        {
            if (!_fieldOf.TryGetValue(column, out var field) || column.IsPrimaryKey)
                continue;

            string? value;
            if (column.IsCreateDateColumn || column.IsLastChangedDateColumn)
                value = column.IsDateColumn ? _di.CurrentTimestamp : null;
            else if (column.IsModifiedDateColumn || column.IsModifiedUserColumn || column.IsInactiveReasonColumn || column == inactiveDate || (m.HasSoftDelete && column == m.DeletedDateColumn))
                value = "NULL";
            else if (column == active)
                value = column.IsStringColumn ? (activeNegative ? "'0'" : "'1'") : _di.BooleanLiteral(!activeNegative);
            else if (column.IsAdminFlagColumn || (m.HasSoftDelete && column == m.IsDeletedColumn))
                value = column.IsStringColumn ? "'False'" : _di.BooleanLiteral(false);
            else if (column.IsCreateUserColumn)
                value = column.IsStringColumn ? "''" : "NULL";
            else if (overrides.Contains(column))
            {
                bound.Add(field);
                value = _di.Placeholder(bound.Count);
            }
            else
                value = "src." + _di.Quote(column.DbName);

            if (value is null)
                continue;
            columns.Add(_di.Quote(column.DbName));
            values.Add(value);
        }

        string key = _di.Quote(Key.Column.DbName);
        string sql = $"INSERT INTO {Table} ({string.Join(", ", columns)}) SELECT {string.Join(", ", values)} FROM {Table} AS src WHERE src.{key} = {_di.Placeholder(bound.Count + 1)}"
            + (Returning ? $" RETURNING {key}" : "");
        return new RustClone(sql, bound);
    }
}
