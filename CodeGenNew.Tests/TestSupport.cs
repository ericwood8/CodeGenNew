using System.Data;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> Where the repository's shipped templates are, found by walking up from the test binaries to CodeGenNew.slnx. </summary>
internal static class Repo
{
    public static string Root { get; } = FindRoot();
    public static string TemplatesDirectory => Path.Combine(Root, "Templates");
    public static string Template(string fileName) => Path.Combine(TemplatesDirectory, fileName);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeGenNew.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("CodeGenNew.slnx was not found above " + AppContext.BaseDirectory);
    }
}

/// <summary> A scratch folder that is deleted when the test ends. </summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CodeGenNewTests_" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name, string content)
    {
        string full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { /* a test must not fail on cleanup */ }
    }
}

internal static class Expect
{
    /// <summary> Assert.Contains with the arguments in the order they read in a test ("text contains part") and a failure
    /// message that shows what was searched for and what the text held. </summary>
    public static void Contains(string text, string part) =>
        Assert.Contains(part, text, $"Expected to find:\n  {part}\nin:\n{text}");

    public static void DoesNotContain(string text, string part) =>
        Assert.DoesNotContain(part, text, $"Did not expect to find:\n  {part}\nin:\n{text}");
}

/// <summary> Builds TableModels by hand (no database) that look like what CodeGenNew.SchemaIntrospection would read. </summary>
internal static class Sample
{
    public static ColumnModel Column(
        string name, SqlDbType type, bool nullable = false, bool primaryKey = false, bool identity = false,
        int? characters = null, int? precision = null, int? scale = null, string? defaultSql = null, int ordinal = 0,
        bool modifiedUserColumn = false, bool createDateColumn = false, bool createUserColumn = false,
        bool inUniqueIndex = false)
    {
        bool isText = type is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar;
        bool isUnicode = type is SqlDbType.NChar or SqlDbType.NVarChar;
        string declaration = type switch
        {
            SqlDbType.NVarChar => $"nvarchar({characters ?? 50})",
            SqlDbType.VarChar => $"varchar({characters ?? 50})",
            SqlDbType.Char => $"char({characters ?? 1})",
            SqlDbType.Decimal => $"decimal({precision ?? 18},{scale ?? 0})",
            _ => type.ToString().ToLowerInvariant()
        };
        return new ColumnModel
        {
            Name = name,
            QuotedName = "[" + name + "]",
            SqlType = type,
            SqlTypeDeclaration = declaration,
            // the schema reader reports the length in BYTES, so an nvarchar(100) column has MaxLength 200
            MaxLength = isText ? (isUnicode ? (characters ?? 50) * 2 : characters ?? 50) : null,
            Precision = precision,
            Scale = scale,
            IsNullable = nullable,
            OrdinalPosition = ordinal,
            IsIdentity = identity,
            IsPrimaryKey = primaryKey,
            IsInUniqueIndex = inUniqueIndex,
            DatabaseDefaultSql = defaultSql,
            IsIntegerColumn = type is SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt or SqlDbType.TinyInt,
            IsStringColumn = isText || type is SqlDbType.Text or SqlDbType.NText,
            IsDateColumn = type is SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime,
            IsBooleanColumn = type == SqlDbType.Bit,
            IsAuditColumn = name.IsAuditColumn(),
            IsModifiedUserColumn = modifiedUserColumn,
            IsCreateDateColumn = createDateColumn,
            IsCreateUserColumn = createUserColumn,
            ParameterName = "@p" + name
        };
    }

    public static ForeignKeyModel ForeignKey(string column, string parentTable, string parentKey, params string[] parentDisplayColumns) => new()
    {
        ConstraintName = $"FK_{column}_{parentTable}",
        ReferencingColumns = [column],
        ReferencedSchema = "dbo",
        ReferencedTable = parentTable,
        ReferencedColumns = [parentKey],
        ReferencedDisplayColumns = [.. parentDisplayColumns]
    };

    /// <summary> Like ForeignKey, but from the parent's side: a child table's FK column pointing back at
    /// this table's key (TableModel.ChildForeignKeys). </summary>
    public static ChildForeignKeyModel ChildForeignKey(string childTable, string childColumn, string parentKey) => new()
    {
        ConstraintName = $"FK_{childColumn}_{childTable}",
        ReferencingSchema = "dbo",
        ReferencingTable = childTable,
        ReferencingColumns = [childColumn],
        ReferencedColumns = [parentKey]
    };

    public static TableModel Table(string name, List<ColumnModel> columns, List<ForeignKeyModel>? foreignKeys = null,
        List<object?[]>? rows = null, List<ChildForeignKeyModel>? childForeignKeys = null) => new()
    {
        SchemaName = "dbo",
        TableName = name,
        QuotedName = $"[dbo].[{name}]",
        Columns = columns,
        PrimaryKeyColumns = columns.Where(c => c.IsPrimaryKey).ToList(),
        ForeignKeys = foreignKeys ?? [],
        ChildForeignKeys = childForeignKeys ?? [],
        // what the schema reader fills in when a template's .tt.config asks for referenced display columns
        HasReferencedDisplayColumns = foreignKeys?.Any(f => f.ReferencedDisplayColumns.Count > 0) ?? false,
        DisplayColumns = columns.SelectDisplayColumns((foreignKeys ?? []).SelectMany(f => f.ReferencingColumns).ToList()),
        HasRowData = rows is not null,
        Rows = rows ?? []
    };

    /// <summary> Like TimeEntry's E_DonateLeave: identity key, two foreign keys to the same parent, a date, an int, a note. </summary>
    public static TableModel DonateLeave() => Table("E_DonateLeave",
    [
        Column("DonateLeaveId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("DonateFrom_EmployeeId", SqlDbType.Int, ordinal: 2),
        Column("DonateTo_EmployeeId", SqlDbType.Int, ordinal: 3),
        Column("WhenDonated", SqlDbType.DateTime, defaultSql: "(getdate())", ordinal: 4),
        Column("HoursDonated", SqlDbType.Int, ordinal: 5),
        Column("Note", SqlDbType.NVarChar, nullable: true, characters: 100, ordinal: 6)
    ],
    [
        ForeignKey("DonateFrom_EmployeeId", "Employee", "EmployeeId", "Name"),
        ForeignKey("DonateTo_EmployeeId", "Employee", "EmployeeId", "Name")
    ]);

    /// <summary> Like TimeEntry's Holiday: a text Name, no IsActive, a string foreign key to a lookup table. </summary>
    public static TableModel Holiday() => Table("Holiday",
    [
        Column("HolidayId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("SY_IsoCountry_Alpha3Code", SqlDbType.Char, characters: 3, ordinal: 2),
        Column("Date", SqlDbType.Date, ordinal: 3),
        Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 4),
        Column("SY_DisplayId", SqlDbType.Int, nullable: true, ordinal: 5)
    ],
    [
        ForeignKey("SY_IsoCountry_Alpha3Code", "SY_ISOCountry", "Alpha3Code"),
        ForeignKey("SY_DisplayId", "SY_Display", "SY_DisplayId")
    ]);

    /// <summary> Like TimeEntry's DepartmentTeam: Name + IsActive (a "name/active" table) hanging off one parent. </summary>
    public static TableModel DepartmentTeam() => Table("DepartmentTeam",
    [
        Column("DepartmentTeamId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("DepartmentId", SqlDbType.Int, ordinal: 2),
        Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 3),
        Column("IsActive", SqlDbType.Bit, defaultSql: "((1))", ordinal: 4)
    ],
    [ForeignKey("DepartmentId", "Department", "DepartmentId", "Name")]);

    /// <summary> Like TimeEntry's Department: a plain int-keyed table that DepartmentTeam hangs off of
    /// (TableModel.ChildForeignKeys), for the WinUI3_DetailMasterScreen tests. </summary>
    public static TableModel DepartmentWithTeams() => Table("Department",
    [
        Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2)
    ],
    childForeignKeys: [ChildForeignKey("DepartmentTeam", "DepartmentId", "DepartmentId")]);

    /// <summary> Like TimeEntry's SY_Role lookup: an id and a Name, with three rows. </summary>
    public static TableModel Roles() => Table("SY_Role",
    [
        Column("SY_RoleId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ],
    rows: [[1, "Admin"], [2, "Human Resources"], [3, "Time off in lieu"]]);

    /// <summary> A table keyed by a database-assigned uniqueidentifier, plus two nullable text columns. </summary>
    public static TableModel AccountRef() => Table("AccountRef",
    [
        Column("ListID", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 1),
        Column("FullName", SqlDbType.VarChar, nullable: true, characters: 255, ordinal: 2),
        Column("AccountRefID", SqlDbType.UniqueIdentifier, primaryKey: true, defaultSql: "(newsequentialid())", ordinal: 3)
    ]);

    /// <summary> A table keyed by a text code the person types (a natural key). </summary>
    public static TableModel NaturalKey() => Table("Country",
    [
        Column("Code", SqlDbType.Char, primaryKey: true, characters: 3, ordinal: 1),
        Column("Label", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ]);

    /// <summary> A table with a two-column primary key. </summary>
    public static TableModel CompositeKey() => Table("Junction",
    [
        Column("LeftId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Column("RightId", SqlDbType.Int, primaryKey: true, ordinal: 2)
    ]);

    /// <summary> A table with a ModifiedUserColumn match (e.g. ModifiedBy/UpdatedBy): who last edited the row. </summary>
    public static TableModel WithModifiedByColumn() => Table("Ticket",
    [
        Column("TicketId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("Subject", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Column("ModifiedBy", SqlDbType.NVarChar, nullable: true, characters: 50, ordinal: 3, modifiedUserColumn: true)
    ]);

    /// <summary> Like a real dbo.NameBaseGroupXref table found on a second production database (confirmed against a live database,
    /// 2026-09-25): a many-to-many junction table shaped around a surrogate identity primary key rather
    /// than a natural composite one -- ID is the PK, NameBaseID/GroupID are plain (non-key) foreign keys,
    /// plus CreateDate/CreateUser audit columns. This turned out to be the real-world shape, not the
    /// composite-key one originally assumed -- see CompositeKeyJunction for that shape. </summary>
    public static TableModel JunctionWithSurrogateKey() => Table("NameBaseGroupXref",
    [
        Column("ID", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("NameBaseID", SqlDbType.Int, ordinal: 2),
        Column("GroupID", SqlDbType.Int, ordinal: 3),
        Column("CreateDate", SqlDbType.DateTime, defaultSql: "(getdate())", ordinal: 4, createDateColumn: true),
        Column("CreateUser", SqlDbType.VarChar, characters: 50, ordinal: 5, createUserColumn: true)
    ],
    [
        ForeignKey("NameBaseID", "NameBase", "ID", "Name"),
        ForeignKey("GroupID", "Groups", "ID", "ShortDescr")
    ]);

    /// <summary> Like TimeEntry's Employee: a plain int-keyed lookup parent, the FK target of DonateLeave()
    /// and TimeSheetWithEmployeeAndDetail() below -- for cross-template consistency tests (does a component
    /// call the method TS_Service actually generates for this same table). </summary>
    public static TableModel Employee() => Table("Employee",
    [
        Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2)
    ]);

    /// <summary> Like TimeEntry's real E_TimeSheet/E_TimeSheetDetail pair (see Docs/specs.md section 11's
    /// TS_DetailMasterComponent entry): an int-keyed table with both a foreign key to a lookup parent
    /// (Employee) and a child table hanging off it (TimeSheetDetail) -- exercises the lookup-dropdown and
    /// child-grid code paths together. </summary>
    public static TableModel TimeSheetWithEmployeeAndDetail() => Table("TimeSheet",
    [
        Column("TimeSheetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("WhenEntered", SqlDbType.DateTime, ordinal: 2),
        Column("EmployeeId", SqlDbType.Int, ordinal: 3)
    ],
    [ForeignKey("EmployeeId", "Employee", "EmployeeId", "Name")],
    childForeignKeys: [ChildForeignKey("TimeSheetDetail", "TimeSheetId", "TimeSheetId")]);

    /// <summary> The other real-world junction-table shape: a natural composite key made of the two FK
    /// columns themselves, no separate surrogate id. </summary>
    public static TableModel CompositeKeyJunction() => Table("UserRole",
    [
        Column("UserId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Column("RoleId", SqlDbType.Int, primaryKey: true, ordinal: 2)
    ],
    [
        ForeignKey("UserId", "User", "UserId", "Name"),
        ForeignKey("RoleId", "Role", "RoleId", "Name")
    ]);

    /// <summary> Like a real dbo.Products table found on a second production database: one column covered by two differently-named FK constraints that both
    /// point at the same parent table. A generator that keys a lookup by column name (instead of grouping) throws
    /// "An item with the same key has already been added" building that lookup. </summary>
    public static TableModel DuplicateForeignKeyColumn() => Table("Product",
    [
        Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("ProductTypeId", SqlDbType.Int, ordinal: 2),
        Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3)
    ],
    [
        new ForeignKeyModel
        {
            ConstraintName = "FK_Product_ProductTypeId_ProductType",
            ReferencingColumns = ["ProductTypeId"],
            ReferencedSchema = "dbo",
            ReferencedTable = "ProductType",
            ReferencedColumns = ["ProductTypeId"],
            ReferencedDisplayColumns = ["Name"]
        },
        new ForeignKeyModel
        {
            ConstraintName = "FK_Product_ProductTypeId_ProductType_Legacy",
            ReferencingColumns = ["ProductTypeId"],
            ReferencedSchema = "dbo",
            ReferencedTable = "ProductType",
            ReferencedColumns = ["ProductTypeId"],
            ReferencedDisplayColumns = ["Name"]
        }
    ]);

    /// <summary> A "name/active" table (Name + IsActive) that is ALSO the parent side of a foreign key --
    /// exercises TS_DetailMasterComponent.tt's/WinUI3_DetailMasterScreen.tt's refusal order (child tables
    /// exist, but the name/active shape should still refuse it, not silently pass that check). </summary>
    public static TableModel NameActiveTableWithChildren() => Table("Team",
    [
        Column("TeamId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Column("IsActive", SqlDbType.Bit, defaultSql: "((1))", ordinal: 3)
    ],
    childForeignKeys: [ChildForeignKey("TeamMember", "TeamId", "TeamId")]);
}
