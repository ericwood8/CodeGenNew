using System.Globalization;

namespace CodeGenNew.Core;

/// <summary> A parent a row of the table points at whose delete rule refuses the delete while a row uses it: the parent's own DELETE must answer 400. </summary>
public sealed record ApiTestUsedParent(ColumnModel Column, ForeignKeyModel Key, string Route);

/// <summary> What the integration test class of one table (<c>API_Test</c>) checks and with which values: the route and the key as it goes into a route, the body of a new row (one value for every column that cannot be NULL;
/// free text gets a unique ending, a foreign key the key of the first row of the parent's API), the one column the update step changes, the sorts the search offers, whether the table clones, the unique text column,
/// the roles a rule lists, and the limits the validators enforce. The template only writes the text. </summary>
public sealed class ApiTestPlan
{
    private readonly TableModel _model;
    private readonly ProjectSettings _project;

    private ApiTestPlan(TableModel model, ProjectSettings project)
    {
        _model = model;
        _project = project;
        Singular = model.TableName;
        Route = "/api/" + Singular.Pluralize().ToLowerInvariant();
        var key = model.PrimaryKeyColumns[0];
        KeyJson = JsonNames.Camel(key.Name);
        KeyType = KeyType.Of(model)!;
        IntKey = KeyType.Kind == KeyKind.Int;
        KeyCs = IntKey ? "long" : "string";                                // a guid travels as the text of the JSON
        IdRef = IntKey ? "id" : "Uri.EscapeDataString(id)";                // the key as it goes into a route
        AbsentKey = KeyType.Kind switch                                    // a key no row has, for the 404 test
        {
            KeyKind.Int => "int.MaxValue",
            KeyKind.Guid => "Guid.NewGuid()",
            _ => $"Uri.EscapeDataString(new string('~', {Math.Max(1, Math.Min(key.CharacterLength > 0 ? key.CharacterLength : 3, 12))}))"
        };
        BodyColumns = ApiSampleValues.BodyColumns(model);
        // EffectiveDatedTables: a new row also sends its last day (the column may allow NULL, which would be an open period that overlaps every row made after it)
        if (EffectiveDatePlan.For(model, project) is { } effective && !BodyColumns.Any(c => c.Name.EqualsIgnoreCase(effective.To.Name)))
            BodyColumns = BodyColumns.Append(effective.To).OrderBy(c => c.OrdinalPosition).ToList();

        // the one column the update step changes: a free text column, else a bit column, else a whole number that has room to go up by one
        Changed = BodyColumns.FirstOrDefault(c => IsFreeText(c) && c.CharacterLength > 0)
            ?? BodyColumns.FirstOrDefault(c => c.IsBooleanColumn && !c.IsPrimaryKey)
            ?? BodyColumns.FirstOrDefault(HasRoomToGrow);
        ChangedJson = Changed is null ? "" : JsonNames.Camel(Changed.Name);
        ChangedType = Changed is null ? "" : Changed.IsStringColumn ? "string" : Changed.IsBooleanColumn ? "bool" : "long";

        // the same names the search sorts by (SearchPlan), a clone the API offers (CloneShape: an int key the database numbers), and a unique index on one free text column
        SortNames = model.HasSearchApi(project) ? SearchPlan.For(model, []).Sorts.Select(s => s.Name.ToLowerInvariant()).Distinct().ToList() : [];
        CanClone = IntKey && CloneShape.CanClone(model, project);
        UniqueText = model.Indexes.Where(i => i.IsUnique && i.Columns.Count == 1)
            .Select(i => BodyColumns.FirstOrDefault(c => IsFreeText(c) && c.CharacterLength > 0 && (c.Name == i.Columns[0] || c.DbName == i.Columns[0])))
            .FirstOrDefault(c => c is not null);

        // ReadRoles and WriteRoles: the roles a rule lists (the administrator role first); a table scoped to a person is not written by a role with no owner
        WriteRoles = WriteRolePlan.For(project, model);
        ReadRoles = WriteRolePlan.ReadFor(project, model);
        ScopedToPerson = OwnedTable.IsScoped(model, project);

        // the parents a row of this table points at whose delete rule refuses the delete while a row uses them: the parent's own DELETE must answer 400, so a screen can say "in use"
        UsedParents = BodyColumns.Where(c => !c.IsPrimaryKey)
            .Select(c => (Column: c, Key: model.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0] == c.Name && f.ReferencedColumns.Count == 1)))
            .Where(x => x.Key is not null && !(project.ApiAuth && project.TenantTable is { } tenantName && tenantName.EqualsIgnoreCase(x.Key.ReferencedTable)) && x.Key.DeleteRule.RefusesInUse() && ParentRoute(x.Column, out string _).Length > 0 && !project.NoApiFor(x.Key.ReferencedTable, x.Key.ReferencedLookupShape))
            .Take(3).Select(x => new ApiTestUsedParent(x.Column, x.Key!, ParentRoute(x.Column, out string _))).ToList();

        // limits the validators enforce, for the 400 tests
        LongText = project.ApiValidation ? BodyColumns.FirstOrDefault(c => IsFreeText(c) && c.CharacterLength > 0) : null;
        if (project.ApiValidation)
        {
            foreach (var c in BodyColumns.Where(c => !c.IsPrimaryKey && c.IsIntegerColumn))
            {
                if (project.RangeFor(c) is { } range && range.Max < NumericClassifier.TypeRange(c.SqlType).Max && ParentRoute(c, out string _).Length == 0)
                {
                    Ranged = c;
                    OverLimit = range.Max + 1;
                    break;
                }
            }
        }
    }

    /// <summary> The plan for a table the ASP.NET API writes a plain CRUD API for (any key), or null for any other table. </summary>
    public static ApiTestPlan? For(TableModel model, ProjectSettings project) => model.HasCrudApiOfAnyKey(project) ? new ApiTestPlan(model, project) : null;

    public string Singular { get; }
    public string Route { get; }
    public string KeyJson { get; }
    public KeyType KeyType { get; }
    public bool IntKey { get; }
    public string KeyCs { get; }
    public string IdRef { get; }
    public string AbsentKey { get; }
    public IReadOnlyList<ColumnModel> BodyColumns { get; }

    /// <summary> The column the update step changes, with its JSON name and the C# type its value has in the test (<c>string</c>, <c>bool</c> or <c>long</c>); null (and blanks) when the table has none. </summary>
    public ColumnModel? Changed { get; }
    public string ChangedJson { get; }
    public string ChangedType { get; }

    public IReadOnlyList<string> SortNames { get; }
    public bool CanClone { get; }
    public ColumnModel? UniqueText { get; }
    public IReadOnlyList<string> WriteRoles { get; }
    public IReadOnlyList<string> ReadRoles { get; }
    public bool ScopedToPerson { get; }
    public IReadOnlyList<ApiTestUsedParent> UsedParents { get; }

    /// <summary> A free text column the validators limit to its length (with <c>ApiValidation</c>), or null. </summary>
    public ColumnModel? LongText { get; }

    /// <summary> A whole-number column whose CHECK range is narrower than its type (with <c>ApiValidation</c>) and the first value past it, or null. </summary>
    public ColumnModel? Ranged { get; }
    public long OverLimit { get; }

    /// <summary> A text value that is not a fixed format: it can take a unique ending. </summary>
    private bool IsFreeText(ColumnModel c) => c.IsStringColumn && !c.HasChoices && !c.IsPrimaryKey && c.Notation is null
        && !_model.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0] == c.Name)
        && !ApiSampleValues.HasFixedFormat(c, _project, _model.TableName);

    /// <summary> The list route of the parent a single-column foreign key points at, and the JSON name of its key; blank when the parent has no list route to take a row from (itself, an enum, a column of another kind). </summary>
    public string ParentRoute(ColumnModel c, out string parentKey)
    {
        parentKey = "";
        var fk = _model.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0] == c.Name && f.ReferencedColumns.Count == 1);
        if (fk is null || fk.ReferencedTable == _model.TableName || !(c.IsIntegerColumn || c.IsStringColumn || c.IsGuidColumn) || _project.NoRepository(fk.ReferencedTable, fk.ReferencedLookupShape) == true)
            return "";
        parentKey = JsonNames.Camel(fk.ReferencedColumns[0]);
        return "/api/" + fk.ReferencedTable.Pluralize().ToLowerInvariant();
    }

    /// <summary> The table a foreign key column points at: its ApiTests class is where a row for an empty parent comes from. </summary>
    private string ParentTable(ColumnModel c) => _model.ForeignKeys.First(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0] == c.Name).ReferencedTable;

    private bool HasRoomToGrow(ColumnModel c) => c.IsIntegerColumn && !c.IsPrimaryKey && ParentRoute(c, out string _).Length == 0
        && (_project.RangeFor(c) is not { } range || range.Max > long.Parse(ApiSampleValues.Value(c, _project, _model.TableName), CultureInfo.InvariantCulture));

    /// <summary> The C# expression of the value a new row's body holds for <paramref name="c"/>. </summary>
    public string Expression(ColumnModel c)
    {
        string sample = ApiSampleValues.Value(c, _project, _model.TableName);
        if (c.IsPrimaryKey)
        {
            if (KeyType.Kind == KeyKind.Guid)
                return "Guid.NewGuid()";
            if (KeyType.Kind == KeyKind.Text)
                return $"ApiFixture.NewCode({(c.CharacterLength > 0 ? c.CharacterLength : 8)})";
            if (c.IsIdentity)
                return sample;
            var range = NumericClassifier.TypeRange(c.SqlType);
            long high = Math.Min(range.Max, 2_000_000_000);
            return $"ApiFixture.NewKey({Math.Max(1, high / 2).ToString(CultureInfo.InvariantCulture)}, {high.ToString(CultureInfo.InvariantCulture)})";
        }
        // EffectiveDatedTables: each new row is a one-day period of its own long before real data, so rows of different tests never overlap (the last day is the first day)
        if (EffectiveDatePlan.For(_model, _project) is { } effective)
        {
            if (c.Name.EqualsIgnoreCase(effective.From.Name))
                return "ApiFixture.NextDay()";
            if (c.Name.EqualsIgnoreCase(effective.To.Name))
                return "ApiFixture.SameDay()";
        }
        if (IsFreeText(c))
            return $"ApiFixture.Unique({ApiSampleValues.Json(ApiSampleValues.Text(c, _project, _model.TableName))}, {c.CharacterLength})";
        string parentRoute = ParentRoute(c, out string parentKey);
        if (parentRoute.Length > 0)
            return $"await ApiFixture.{(c.IsStringColumn || c.IsGuidColumn ? "ExistingCodeAsync" : "ExistingKeyAsync")}({ApiSampleValues.Json(parentRoute)}, {ApiSampleValues.Json(parentKey)}, {sample}, {ApiSampleValues.Json(ParentTable(c))})";
        return sample;
    }
}
