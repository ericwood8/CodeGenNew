using System.Data;

namespace CodeGenNew.Core;

/// <summary> What the API class of one table (<c>API_Crud</c>) says on top of <c>CrudApi</c>: which base it derives from and with which type arguments, the key, the date the list comes newest first by, the lookup, the roles, the owner
/// scope, the editable children, the search and sort of the search list protocol, the audit dates it stamps, the rules of the user table and the gates. The template only writes the text. A table the API cannot be written for has a
/// <see cref="Refusal"/> and nothing else is read. </summary>
public sealed class CrudApiPlan
{
    private CrudApiPlan(TableModel model, ProjectSettings project)
    {
        Entity = model.TableName;
        var keyColumn = model.PrimaryKeyColumns.Count == 1 ? model.PrimaryKeyColumns[0] : null;
        KeyType = model.PrimaryKeyType;
        bool noRepository = project.NoRepositoryFor(model.TableName, model.LookupShape);
        Supported = KeyType is not null && !noRepository && (!model.IsNameActiveTable || keyColumn!.IsInt32Column);   // a Name and IsActive table stays int-keyed

        if (noRepository)
            Refusal = $"API_Crud writes no API for [{model.SchemaName}].[{model.TableName}]: it is an enum or lookup table (an enum, a *Type lookup table, or a table with no entity class), so it has no repository for the handlers to use.";
        else if (!Supported)
            Refusal = $"API_Crud needs a table with a single int, uniqueidentifier or text primary key (a Name and IsActive table needs an int one), but [{model.SchemaName}].[{model.TableName}] has "
                + (model.PrimaryKeyColumns.Count == 0 ? "no primary key." : model.PrimaryKeyColumns.Count > 1
                    ? "a composite primary key (" + string.Join(", ", model.PrimaryKeyColumns.Select(c => c.Name)) + ")."
                    : $"a {model.PrimaryKeyColumns[0].SqlTypeDeclaration} primary key.");
        if (!Supported)
            return;

        _model = model;

        // Dtos: the API takes <Table>Input (CS_DtoInput) and answers <Table>Dto (CS_Dto); DtoShape is the one rule the DTO templates ask too
        UsesDtos = !model.IsNameActiveTable && DtoShape.Uses(model, project);

        // A table with a Name and an IsActive column: NameActiveRepo is its repository, NameActiveCrudApi its base (no clone, no search; CloneShape leaves it out).
        NameActive = model.IsNameActiveTable;
        // A table with a database-assigned int key and a clone routine gets POST <route>/{id}/clone (CloneShape; CS_Repo writes CloneAsync for the same tables).
        // a log table (LogTables) is only read: no create, update, delete or clone route
        var log = NameActive ? null : LogTablePlan.For(model, project);
        ReadOnly = log is not null;
        CanClone = !NameActive && !ReadOnly && CloneShape.CanClone(model, project);
        CanFindByName = model.CanFindByName;

        KeyProperty = keyColumn?.Name ?? "";
        KeyCSharp = KeyType?.CSharp ?? "int";
        // the int key keeps the original two-argument base and the non-generic LookupItem; any other key names its type
        ApiBase = NameActive ? "NameActiveCrudApi" : "CrudApi";
        KeyArgument = NameActive || KeyCSharp == "int" ? "" : ", " + KeyCSharp;
        // an API with an input and an output type names both after its repository (and its key, unless it is an int)
        DtoArguments = UsesDtos ? $", {model.TableName}Input, {model.TableName}Dto" : "";
        LookupItem = KeyCSharp == "int" ? "LookupItem" : "LookupItem<" + KeyCSharp + ">";

        // ApiAuth with WriteRoles or TableWriteRoles: the roles that may write this table (empty: any signed-in person)
        WriteRoles = WriteRolePlan.For(project, model);
        ReadRoles = WriteRolePlan.ReadFor(project, model);

        // ApiAuth with OwnedTables: a caller reaches only the rows of the owners the IOwnerScope allows (the owner column; a Name and IsActive table is never scoped)
        OwnerColumn = !NameActive ? OwnedTable.OwnerColumn(model, project) : null;
        OwnerUnusable = !NameActive && OwnedTable.IsListedButUnusable(model, project);
        // a table with no owner column of its own, hanging off an owned table (the detail rows of a time sheet), belongs to the owner of its parent row
        OwnerLink = !NameActive ? OwnedTable.Through(model, project) : null;

        // EditableChildren: the child tables a form of this table saves in the same request (EditableChild, the one rule the Angular screen asks too)
        EditableChildren = EditableChild.Of(model, project);

        // ListProtocol=Search: the one search box matches SearchBox's columns and the grid sorts by SearchPlan's (the same names the Columns protocol sorts by)
        ListSearch = !NameActive && project.ListSearchProtocol;
        var searchNavigations = ListSearch ? EntityNavigations.Of(model, fk => project.NoNavigation(fk.ReferencedTable, fk.ReferencedLookupShape) ?? false) : new List<EntityNavigation>();
        SearchPlan = ListSearch ? CodeGenNew.Core.SearchPlan.For(model, searchNavigations) : null;
        SearchTerms = ListSearch ? SearchBox.For(model, project, searchNavigations) : [];

        // the column GetAll sorts by: the first date that is always there and that no one sets
        SortColumn = log is { Timestamp: { IsNullable: false } stamp } && stamp.SqlType != System.Data.SqlDbType.DateTimeOffset ? log.Timestamp : NameActive ? null : model.Columns
            .Where(c => IsDate(c) && !c.IsNullable && !c.IsComputed && !c.IsAuditColumn && !c.IsAuditDateColumn)
            .OrderBy(c => c.OrdinalPosition)
            .FirstOrDefault();

        // GET <route>/lookup answers { id, name, isActive } (only the key, the display column and the flag are read) for a table with a display column; a Name and IsActive table always has one.
        DisplayColumn = !NameActive ? model.DisplayColumns.FirstOrDefault() : null;   // the column the screens' drop-downs show
        LookupName = DisplayColumn is null ? ""
            : DisplayColumn.IsStringColumn ? $"c.{DisplayColumn.Name}" + (DisplayColumn.IsNullable ? " ?? \"\"" : "")
            : DisplayColumn.IsNullable ? $"c.{DisplayColumn.Name} == null ? \"\" : c.{DisplayColumn.Name}.ToString()!"
            : $"c.{DisplayColumn.Name}.ToString()";

        // the audit dates no caller sets: a create date the database does not default and the last-changed date on a new row, the modified and last-changed dates on a changed one
        StampOnNew = model.Columns.Where(c => !c.IsComputed && c.IsDateColumn && (c.IsLastChangedDateColumn || (c.IsCreateDateColumn && c.DatabaseDefaultSql is null))).ToList();
        StampOnChange = model.Columns.Where(c => !c.IsComputed && c.IsDateColumn && (c.IsModifiedDateColumn || c.IsLastChangedDateColumn)).ToList();

        // ChangeGates and GuardedColumns (API_Gates): the table's checks run before a row is saved.
        HasGates = GatePlan.HasRules(project, model);

        // TreeTables, SoftDeleteTables and EffectiveDatedTables (API_RowShapes): the table's checks run before a row is saved and before it is deleted.
        RowShapeSave = RowShapeWriter.HasSaveCheck(model, project);
        RowShapeDelete = RowShapeWriter.HasDeleteCheck(model, project);

        // The user table (UserTable with ApiAuth): its API never sends or takes the password hash, lets only an administrator change the administrator and role columns, keeps a sign-in name unique
        // and keeps the last administrator (UserTable, API_UserDirectory).
        UserPlan = UserTable.PlanOf(model, project);
        UserAdmin = UserPlan?.Admin;
        UserRole = UserPlan?.Role;
        UserGuarded = new[] { UserAdmin, UserRole }.Where(c => c is not null).Select(c => c!).ToList();
        UserName = UserPlan?.UserName.CSharpName() ?? "";
        UserHash = UserPlan?.PasswordHash.CSharpName() ?? "";
        UserActive = model.IsActiveColumn?.CSharpName() ?? "";
        UserStoredMembers = string.Join(", ", new[] { $"Hash = u.{UserHash}" }
            .Concat(UserGuarded.Select(c => $"{c.CSharpName()} = u.{c.CSharpName()}"))
            .Concat(UserAdmin is not null && model.IsActiveColumn is not null ? new[] { $"Active = u.{UserActive}" } : []));
    }

    private readonly TableModel? _model;

    private static bool IsDate(ColumnModel c) => c.SqlType is SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime;

    public static CrudApiPlan For(TableModel model, ProjectSettings project) => new(model, project);

    /// <summary> Why no API is written for the table (an enum or lookup table, no usable key), or null. </summary>
    public string? Refusal { get; }

    /// <summary> Whether an API class is written; when it is not, only <see cref="Entity"/>, <see cref="KeyType"/> and <see cref="Refusal"/> mean anything. </summary>
    public bool Supported { get; }

    public string Entity { get; }
    public KeyType? KeyType { get; }
    public bool UsesDtos { get; }
    public bool NameActive { get; }
    public bool CanClone { get; }

    /// <summary> True for a table listed in LogTables: the API class says so and the base registers no write route. </summary>
    public bool ReadOnly { get; }
    public bool CanFindByName { get; }
    public string KeyProperty { get; } = "";
    public string KeyCSharp { get; } = "int";
    public string ApiBase { get; } = "CrudApi";
    public string KeyArgument { get; } = "";
    public string DtoArguments { get; } = "";
    public string LookupItem { get; } = "LookupItem";

    public IReadOnlyList<string> WriteRoles { get; } = [];
    public IReadOnlyList<string> ReadRoles { get; } = [];

    public ColumnModel? OwnerColumn { get; }
    public bool OwnerUnusable { get; }
    public OwnerLink? OwnerLink { get; }

    public IReadOnlyList<ChildForeignKeyModel> EditableChildren { get; } = [];

    public bool ListSearch { get; }
    public SearchPlan? SearchPlan { get; }
    public IReadOnlyList<SearchBoxTerm> SearchTerms { get; } = [];

    public ColumnModel? SortColumn { get; }
    public ColumnModel? DisplayColumn { get; }

    /// <summary> The C# expression of the lookup's name for a row <c>c</c>. </summary>
    public string LookupName { get; } = "";

    public IReadOnlyList<ColumnModel> StampOnNew { get; } = [];
    public IReadOnlyList<ColumnModel> StampOnChange { get; } = [];

    /// <summary> The C# expression of the time a stamp column is set to: <c>DateTimeOffset.Now</c> for a DateTimeOffset, else the base's <c>LocalNow</c>. </summary>
    public static string Now(ColumnModel c) => c.CSharpBase() == "DateTimeOffset" ? "DateTimeOffset.Now" : "LocalNow";

    public bool HasGates { get; }

    /// <summary> Whether the API calls <c>&lt;Table&gt;Shape.CheckSaveAsync</c> before a save (a tree or an effective-dated table). </summary>
    public bool RowShapeSave { get; }

    /// <summary> Whether the API calls <c>&lt;Table&gt;Shape.CheckDeleteAsync</c> before a delete (a tree or a soft-delete table). </summary>
    public bool RowShapeDelete { get; }

    public UserTablePlan? UserPlan { get; }
    public ColumnModel? UserAdmin { get; }
    public ColumnModel? UserRole { get; }

    /// <summary> The administrator and role columns, which only an administrator changes. </summary>
    public IReadOnlyList<ColumnModel> UserGuarded { get; } = [];
    public string UserName { get; } = "";
    public string UserHash { get; } = "";
    public string UserActive { get; } = "";

    /// <summary> The members of the anonymous object that reads the stored user row (<c>Hash = u.PasswordHash, IsAdmin = u.IsAdministrator, Active = u.IsActive</c>). </summary>
    public string UserStoredMembers { get; } = "";

    /// <summary> The value a non-administrator's new row gets for a guarded user column: false for a flag, null for an optional column, else an empty text. </summary>
    public static string UserDefault(ColumnModel c) => c.IsBooleanColumn ? "false" : c.IsNullable ? "null" : "\"\"";

    /// <summary> The C# test that <paramref name="row"/> is an active administrator (<c>row.IsAdministrator == true &amp;&amp; row.IsActive</c>). </summary>
    public string UserIsAdmin(string row, string? active = null) => UserAdmin is null ? "false"
        : $"{row}.{UserAdmin.CSharpName()} == true" + (_model!.IsActiveColumn is null ? "" : $" && {row}.{active ?? UserActive}");
}
