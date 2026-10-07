using System.Data;

namespace CodeGenNew.Core;

public enum SqlDialect
{
    SqlServer,
    PostgreSql,
    MySql,
    Sqlite
}

/// <summary> Everything a template needs to know about one selected table, built fresh by CodeGenNew.SchemaIntrospection each time a table is selected. </summary>
public class TableModel
{
    public required string SchemaName { get; init; }
    /// <summary> The name generated code uses (a C# class, a file, a route). </summary>
    public required string TableName { get; init; }

    /// <summary> The table's real name in the database when the project's naming style changed <see cref="TableName"/> (customer_item -> CustomerItem); null when they are the same. </summary>
    public string? DatabaseTableName { get; init; }

    /// <summary> Columns whose database type CodeGenNew does not map (a PostgreSQL array, geometry ...): they come through as an unsupported object column that an EF Core
    /// context cannot map, so the project lists them in IgnoredColumns. </summary>
    public IEnumerable<ColumnModel> UnsupportedColumns => Columns.Where(c => c.SqlType == System.Data.SqlDbType.Variant);

    /// <summary> The name SQL text uses: the database's own name. </summary>
    public string DbTableName => DatabaseTableName ?? TableName;

    public required string QuotedName { get; init; }

    /// <summary> Which database the table was read from; a template that writes SQL text branches on it (T-SQL vs PostgreSQL). </summary>
    public SqlDialect Dialect { get; init; } = SqlDialect.SqlServer;
    public bool IsReservedWordName { get; init; }
    public bool IsCSharpReservedWordName { get; init; }

    public required List<ColumnModel> Columns { get; init; }

    /// <summary> Ordered; supports composite and GUID primary keys (not just a single int identity column). </summary>
    public required List<ColumnModel> PrimaryKeyColumns { get; init; }
    public bool HasPrimaryKey => PrimaryKeyColumns.Count > 0;

    /// <summary> See PrimaryKeyShape and TemplateConfig.RequiredPrimaryKeyShape (Docs/Reference.md section 3) --
    /// several templates need more than just HasPrimaryKey. </summary>
    /// <summary> What the database says about the table (SQL Server <c>MS_Description</c>, PostgreSQL <c>COMMENT ON TABLE</c>, MySQL <c>COMMENT</c>); null when it says nothing. </summary>
    public string? Description { get; init; }

    /// <summary> True when API_Crud writes an API for the table under these project settings: a single whole-number key, not a name/active table, not one the project says has no repository (an enum). </summary>
    public bool HasCrudApi(ProjectSettings project) =>
        PrimaryKeyShape == PrimaryKeyShape.SingleInt && !IsNameActiveTable && project.NoRepository(TableName, LookupShape) != true;

    /// <summary> True when the table also gets a search endpoint (API_Search): it has an API and is not a bare lookup table. </summary>
    public bool HasSearchApi(ProjectSettings project) => HasCrudApi(project) && !LookupShape.LooksLikeLookup;

    public PrimaryKeyShape PrimaryKeyShape =>
        PrimaryKeyColumns.Count == 0 ? PrimaryKeyShape.None :
        PrimaryKeyColumns.Count > 1 ? PrimaryKeyShape.Composite :
        PrimaryKeyColumns[0].IsGuidColumn ? PrimaryKeyShape.SingleUniqueIdentifier :
        PrimaryKeyColumns[0].IsIntegerColumn ? PrimaryKeyShape.SingleInt :
        PrimaryKeyShape.SingleOther;

    /// <summary> A NOT NULL text column called exactly "Name" plus a NOT NULL bit column called exactly
    /// "IsActive" -- the one shape test repeated identically across API_Crud.tt, CS_Entity.tt, CS_Repo.tt and
    /// the WinUI3 CRUD-screen family (each used to spell it out inline; canonical here so they can't drift
    /// apart). Such a table's repository is a NameActiveRepo, not a GenericRepo -- see
    /// TemplateConfig.RequiresNotNameActiveTable (Docs/Reference.md section 3) for which templates that rules
    /// out and why. Exact-name match, not a SpecialLogicColumns.config pattern rule, matching how the
    /// existing inline checks were always written. </summary>
    public bool IsNameActiveTable => _isNameActiveTable ??=
        Columns.Any(c => c.IsStringColumn && !c.IsNullable && c.Name == "Name") &&
        Columns.Any(c => c.IsBooleanColumn && !c.IsNullable && c.Name == "IsActive");

    private bool? _isNameActiveTable;
    private bool? _isJunctionTable;
    private List<ColumnModel>? _junctionCandidateColumns;
    private List<ForeignKeyModel>? _junctionForeignKeys;

    /// <summary> A column that records the row's creation and a column that records a later change (see <see cref="AuditTableShape"/>): the tables SP_AuditTable is offered for. </summary>
    public bool IsAuditTable => AuditTableShape.IsAuditTable(Columns.Select(c => c.Name));

    /// <summary> True only when the model was built with row data (a template's .tt.config sets NeedsRowData=true,
    /// e.g. SP_Load.tt). Otherwise Rows is empty because it was never read -- not because the table is empty. </summary>
    public bool HasRowData { get; init; }

    /// <summary> The table's current rows, ordered by primary key; each array holds one value per entry of
    /// Columns (same order), with null for SQL NULL (and for computed columns, which are never read). Format a value for T-SQL with SqlLiteral.Format. </summary>
    public List<object?[]> Rows { get; init; } = [];

    /// <summary> True only when the model was built with ForeignKeyModel.ReferencedDisplayColumns filled in (see there). </summary>
    public bool HasReferencedDisplayColumns { get; init; }

    /// <summary> This table's own display columns (see DisplayColumnSelector), in column order. </summary>
    public List<ColumnModel> DisplayColumns { get; init; } = [];

    /// <summary> Whether this table looks like a small lookup table a project would model as an enum (see LookupShape). </summary>
    public LookupShape LookupShape { get; init; }

    public required List<ForeignKeyModel> ForeignKeys { get; init; }
    public bool HasAtLeastOneForeignKey => ForeignKeys.Count > 0;

    /// <summary> Other tables that have a foreign key pointing back at this one -- the mirror image of
    /// ForeignKeys. Always computed (not gated behind a template .tt.config flag), same as ForeignKeys. </summary>
    public required List<ChildForeignKeyModel> ChildForeignKeys { get; init; }
    public bool HasAtLeastOneChildForeignKey => ChildForeignKeys.Count > 0;

    /// <summary> The table's indexes, the primary key's included. </summary>
    public IReadOnlyList<IndexModel> Indexes { get; init; } = [];

    /// <summary> True when some index starts with exactly these columns (database names, in any order), so a lookup or join on them can use it. </summary>
    public bool IsIndexed(IReadOnlyList<string> databaseColumns) => Indexes.Any(index =>
        index.Columns.Count >= databaseColumns.Count
        && index.Columns.Take(databaseColumns.Count).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(databaseColumns));

    public bool IsSelfReferencing(ForeignKeyModel fk) =>
        fk.ReferencedTable.EqualsIgnoreCase(TableName);

    public bool IsForeignKeyMulti(ForeignKeyModel fk) =>
        ForeignKeys.Count(f => f.ReferencedTable.EqualsIgnoreCase(fk.ReferencedTable)) > 1;

    /// <summary> The table's candidate "association" columns for junction-table purposes: every column
    /// except computed columns, audit-classified columns (IsAuditColumn -- Create*/Modif*/Change*/Delete*/
    /// Update*/Activ*/Inactiv*, per AuditColumnClassifier), and a surrogate identity primary key column.
    /// Real junction tables come in two shapes and both need to reduce to exactly these two columns:
    /// a natural composite key (PrimaryKeyColumns = the two FK columns themselves, no separate id) and a
    /// surrogate-id key (an identity PK plus two plain FK columns, e.g. dbo.NameBaseGroupXref's ID +
    /// NameBaseID + GroupID -- confirmed against a real database, 2026-09-25; the surrogate-id shape turned
    /// out to be the one a real table actually used, not the composite-key shape originally assumed). </summary>
    private List<ColumnModel> JunctionCandidateColumns => _junctionCandidateColumns ??=
        Columns.Where(c => !c.IsComputed && !c.IsAuditColumn && !(c.IsIdentity && c.IsPrimaryKey)).ToList();

    /// <summary> A many-to-many "junction"/"bridge" table: exactly two JunctionCandidateColumns, each
    /// individually covered by its own single-column foreign key (to the same parent table or two different
    /// ones -- a self-referencing many-to-many edge, e.g. a "follows" table between rows of the same Users
    /// table, counts too). A composite FK spanning both columns in one constraint does NOT count: that
    /// describes a table whose key duplicates a parent's own composite key, not a many-to-many association.
    /// Purely structural (no name-pattern guessing), unlike most SpecialLogicColumns.config categories. See
    /// The junction editor screen is the UI pattern this supports. </summary>
    public bool IsJunctionTable => _isJunctionTable ??=
        JunctionCandidateColumns.Count == 2 &&
        JunctionCandidateColumns.All(c => ForeignKeys.Any(fk =>
            fk.ReferencingColumns.Count == 1 && fk.ReferencingColumns[0].EqualsIgnoreCase(c.Name)));

    /// <summary> When IsJunctionTable, the two foreign keys that make up the many-to-many association (in
    /// JunctionCandidateColumns order) -- use this instead of ForeignKeys directly, since a junction table
    /// could rarely have another FK that isn't part of the association itself (e.g. a non-audit-named
    /// CreatedByUserId). Empty when IsJunctionTable is false. </summary>
    public List<ForeignKeyModel> JunctionForeignKeys => _junctionForeignKeys ??=
        !IsJunctionTable ? [] : JunctionCandidateColumns
            .Select(c => ForeignKeys.First(fk => fk.ReferencingColumns.Count == 1 && fk.ReferencingColumns[0].EqualsIgnoreCase(c.Name)))
            .ToList();

    /// <summary> Columns SP_Search.tt, API_Search.tt and CS_Repo.tt's SearchAsync all filter on: every string
    /// column (never a long note: ColumnModel.IsLongTextColumn) that isn't audit-classified (IsAuditColumn -- Create*/Modif*/Change*/Delete*/Update*/Activ*/
    /// Inactiv*, per AuditColumnClassifier) -- nobody types into a search box for "who created this row".
    /// Centralized here the same way Pluralizer was
    /// pulled up from three copies, so the three templates -- and every frontend search bar built from this
    /// same list -- can't drift apart on what's searchable. Empty when the table has nothing to filter on
    /// (all-numeric/date/bit columns); SP_Search.tt/API_Search.tt both refuse to generate in that case. </summary>
    public List<ColumnModel> SearchableColumns =>
        Columns.Where(c => c.IsStringColumn && !c.IsAuditColumn && !c.IsLongTextColumn).Take(MaxSearchFields).ToList();

    /// <summary> The most search boxes a search bar gets: a table with 30 text columns would otherwise get an unusable bar and a procedure with 30 filters.
    /// The first eight searchable text columns, in table order, are used. </summary>
    public const int MaxSearchFields = 8;

    // Special-logic, table-level (see Docs/Reference.md section 5). Populated from SpecialLogicColumns.config.
    public bool HasActiveInactivePair { get; init; }
    public ColumnModel? ActiveColumn { get; init; }
    public ColumnModel? InactiveDateColumn { get; init; }

    public bool HasStartEndDatePair { get; init; }
    public ColumnModel? StartDateColumn { get; init; }
    public ColumnModel? EndDateColumn { get; init; }

    public bool HasSoftDelete { get; init; }
    public ColumnModel? IsDeletedColumn { get; init; }
    public ColumnModel? DeletedDateColumn { get; init; }

    /// <summary> Set when the table has all four exact columns DateIn/TimeIn/DateOut/TimeOut
    /// (e.g. the CMS table) -- a separate-date-and-time variant of HasStartEndDatePair that the
    /// pattern-based SpecialLogicColumns.config rules can't express (it needs two columns per side, not
    /// one), so it's detected directly by exact column name instead. </summary>
    public bool HasInOutDateTimePair { get; init; }
    public ColumnModel? InDateColumn { get; init; }
    public ColumnModel? InTimeColumn { get; init; }
    public ColumnModel? OutDateColumn { get; init; }
    public ColumnModel? OutTimeColumn { get; init; }
}
