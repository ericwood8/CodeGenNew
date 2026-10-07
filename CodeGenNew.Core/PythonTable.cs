using System.Data;

namespace CodeGenNew.Core;

/// <summary> One column of a Python table: the attribute name, the JSON name, the Pydantic annotation and the SQLAlchemy column type. <see cref="ReadOnly"/> is a column the database writes (identity or
/// computed, other than the key), which a request body may leave out. </summary>
public sealed record PythonField(ColumnModel Column, string Name, string Json, string Annotation, string SqlAlchemyType, bool ReadOnly)
{
    public bool IsNullable => Column.IsNullable;

    /// <summary> The annotation with <c>| None</c> when the column can be NULL (or the database writes it). </summary>
    public string OptionalAnnotation => IsNullable || ReadOnly ? Annotation + " | None" : Annotation;
}

/// <summary> A filter of the search: the attribute to match, the query-string name. </summary>
public sealed record PythonFilter(PythonField Field, string Query);

/// <summary> A sort the grid may ask for: the name in lower case and the field to order by. </summary>
public sealed record PythonSort(string Name, PythonField Field);

/// <summary> What a Python API needs to know about one table, worked out once from the schema: the SQLAlchemy class, the Pydantic schema, the search and the clone. The templates only lay it out.
/// A column whose type Python has no mapping for here (binary, a type CodeGenNew cannot map) is left out and listed in <see cref="Skipped"/>. </summary>
public sealed class PythonTable
{
    private PythonTable(TableModel model, ProjectSettings project)
    {
        Model = model;
        Class = PythonNames.Pascal(ScreenNames.BaseName(model.TableName));
        Schema = Class + "Schema";
        Module = PythonNames.Snake(model.TableName);
        Route = BlazorNames.ApiRoute(model.TableName);

        var used = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<PythonField>();
        var skipped = new List<ColumnModel>();
        foreach (var column in model.Columns)
        {
            var type = TypeOf(column, model.Dialect);
            if (type is null)
            {
                skipped.Add(column);
                continue;
            }

            string name = PythonNames.Ident(PythonNames.Snake(column.Name));
            for (int n = 2; !used.Add(name); n++)
                name = PythonNames.Ident(PythonNames.Snake(column.Name)) + "_" + n;
            fields.Add(new PythonField(column, name, JsonNames.Camel(column.Name), type.Value.Annotation, type.Value.SqlAlchemy, (column.IsIdentity || column.IsComputed) && !column.IsPrimaryKey));
        }

        Fields = fields;
        Skipped = skipped;
        Key = fields.First(f => f.Column.IsPrimaryKey);
        Written = fields.Where(f => !f.Column.IsIdentity && !f.Column.IsComputed).ToList();
        Updated = Written.Where(f => !f.Column.IsPrimaryKey).ToList();

        var fieldOf = new Dictionary<ColumnModel, PythonField>();
        foreach (var field in fields)
            fieldOf.TryAdd(field.Column, field);

        var plan = SearchPlan.For(model, []);
        Filters = plan.Filters
            .Where(f => f.Column.CSharpBase() == "string" && fieldOf.ContainsKey(f.Column))
            .Select(f => new PythonFilter(fieldOf[f.Column], f.ParameterName)).ToList();
        Sorts = plan.Sorts.Where(s => fieldOf.ContainsKey(s.Column))
            .Select(s => new PythonSort(s.Name.ToLowerInvariant(), fieldOf[s.Column])).ToList();
        DefaultOrder = plan.DefaultOrder.Where(fieldOf.ContainsKey).Select(c => fieldOf[c]).ToList();
        if (DefaultOrder.Count == 0)
            DefaultOrder = [Key];

        if (CloneShape.CanClone(model, project))
            BuildClone();
    }

    public static PythonTable Of(TableModel model, ProjectSettings project) => new(model, project);

    public TableModel Model { get; }

    /// <summary> The SQLAlchemy class: <c>SalesInvoice</c>. </summary>
    public string Class { get; }

    /// <summary> The Pydantic class: <c>SalesInvoiceSchema</c>. </summary>
    public string Schema { get; }

    /// <summary> The module (file) name: <c>sales_invoice</c>. </summary>
    public string Module { get; }

    /// <summary> The route every stack uses for the table: <c>/api/customers</c>. </summary>
    public string Route { get; }

    public IReadOnlyList<PythonField> Fields { get; }
    public IReadOnlyList<ColumnModel> Skipped { get; }
    public PythonField Key { get; }

    /// <summary> The fields an INSERT writes: everything the database does not assign. </summary>
    public IReadOnlyList<PythonField> Written { get; }

    /// <summary> The fields an UPDATE sets: the written ones except the key. </summary>
    public IReadOnlyList<PythonField> Updated { get; }

    public IReadOnlyList<PythonFilter> Filters { get; }
    public IReadOnlyList<PythonSort> Sorts { get; }

    /// <summary> The order that applies without a sort and follows a chosen one: the best display column, then the key. </summary>
    public IReadOnlyList<PythonField> DefaultOrder { get; private set; }

    /// <summary> Null when the table gets no Clone button (<see cref="CloneShape.CanClone"/>); otherwise the value each copied attribute of the new row gets, as Python text, in column order. </summary>
    public IReadOnlyList<(PythonField Field, string Value)>? Clone { get; private set; }

    /// <summary> The fields of a clone that need a free unique value (<c>suggest_free</c>). </summary>
    public IReadOnlyList<PythonField> CloneOverrides { get; private set; } = [];

    /// <summary> The Python annotation and SQLAlchemy type of a column, or null when there is none (the column is left out). </summary>
    public static (string Annotation, string SqlAlchemy)? TypeOf(ColumnModel column, SqlDialect dialect)
    {
        string declaration = column.SqlTypeDeclaration.ToLowerInvariant();
        // A PostgreSQL enum type: the driver has to know the type's name, or it sends the text as varchar and the database refuses it.
        if (dialect == SqlDialect.PostgreSql && column.DbEnumType is { } enumType && column.Choices is { Count: > 0 } choices)
        {
            string[] parts = enumType.Split('.');
            string schema = parts.Length > 1 ? $", schema={PythonNames.Str(parts[0])}" : "";
            return ("str", $"PgEnum({string.Join(", ", choices.Select(PythonNames.Str))}, name={PythonNames.Str(parts[^1])}{schema}, create_type=False)");
        }

        switch (column.SqlType)
        {
            case SqlDbType.Int:
                return ("int", declaration.Contains("unsigned") ? "BigInteger" : "Integer");
            case SqlDbType.BigInt:
                return ("int", "BigInteger");
            case SqlDbType.SmallInt when declaration == "year":
                return null;
            case SqlDbType.SmallInt or SqlDbType.TinyInt:
                return ("int", "SmallInteger");
            case SqlDbType.Bit:
                return ("bool", "Boolean");
            case SqlDbType.Money or SqlDbType.SmallMoney when dialect == SqlDialect.PostgreSql:
                return null;
            case SqlDbType.Money or SqlDbType.SmallMoney:
                return ("Decimal", "Numeric(19, 4)");
            case SqlDbType.Decimal:
                return ("Decimal", column.Precision is { } p && p > 0 ? $"Numeric({p}, {column.Scale ?? 0})" : "Numeric");
            case SqlDbType.Float or SqlDbType.Real:
                return ("float", "Float");
            case SqlDbType.Date:
                return ("date", "Date");
            case SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime:
                return ("datetime", "DateTime");
            case SqlDbType.DateTimeOffset:
                return ("datetime", "DateTime(timezone=True)");
            case SqlDbType.Time:
                return ("time", "Time");
            case SqlDbType.UniqueIdentifier:
                return ("UUID", "Uuid");
            case SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar:
                int length = column.CharacterLength;
                return ("str", length > 0 ? $"String({length})" : "Text");
            case SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml:
                return ("str", "Text");
            default:
                return null;
        }
    }

    // The same rules as the other stacks' clone (RustTable.BuildClone): dates the row sets, audit and soft-delete columns cleared, the active flag reset, a unique text value made free.
    private void BuildClone()
    {
        var m = Model;
        var overrides = CloneShape.OverrideColumns(m);
        var active = m.HasActiveInactivePair ? m.ActiveColumn : null;
        var inactiveDate = m.HasActiveInactivePair ? m.InactiveDateColumn : null;
        bool activeNegative = active is not null && active.IsInactive;
        var values = new List<(PythonField, string)>();
        var freed = new List<PythonField>();

        foreach (var column in CloneShape.Copyable(m))
        {
            var field = Fields.FirstOrDefault(f => f.Column == column);
            if (field is null || column.IsPrimaryKey)
                continue;

            string? value;
            if (column.IsCreateDateColumn || column.IsLastChangedDateColumn)
                value = column.IsDateColumn ? (column.SqlType == SqlDbType.Date ? "date.today()" : "datetime.now()") : null;
            else if (column.IsModifiedDateColumn || column.IsModifiedUserColumn || column.IsInactiveReasonColumn || column == inactiveDate || (m.HasSoftDelete && column == m.DeletedDateColumn))
                value = "None";
            else if (column == active)
                value = column.IsStringColumn ? (activeNegative ? "\"0\"" : "\"1\"") : (activeNegative ? "False" : "True");
            else if (column.IsAdminFlagColumn || (m.HasSoftDelete && column == m.IsDeletedColumn))
                value = column.IsStringColumn ? "\"False\"" : "False";
            else if (column.IsCreateUserColumn)
                value = column.IsStringColumn ? "\"\"" : "None";
            else if (overrides.Contains(column))
            {
                freed.Add(field);
                int length = column.CharacterLength;
                value = $"suggest_free(session, {Class}.{field.Name}, source.{field.Name}, {length})";
                if (column.IsNullable)
                    value = $"None if source.{field.Name} is None else {value}";
            }
            else
                value = "source." + field.Name;

            if (value is not null)
                values.Add((field, value));
        }

        Clone = values;
        CloneOverrides = freed;
    }
}
