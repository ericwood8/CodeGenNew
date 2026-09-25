using System.Data;

namespace CodeGenNew.Core;

/// <summary> Everything a template needs to know about one selected table, built fresh by CodeGenNew.SchemaIntrospection each time a table is selected. </summary>
public class TableModel
{
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public required string QuotedName { get; init; }
    public bool IsReservedWordName { get; init; }
    public bool IsCSharpReservedWordName { get; init; }

    public required List<ColumnModel> Columns { get; init; }

    /// <summary> Ordered; supports composite and GUID primary keys (not just a single int identity column). </summary>
    public required List<ColumnModel> PrimaryKeyColumns { get; init; }
    public bool HasPrimaryKey => PrimaryKeyColumns.Count > 0;

    /// <summary> See PrimaryKeyShape and TemplateConfig.RequiredPrimaryKeyShape (Docs/specs.md section 5.3) --
    /// several templates need more than just HasPrimaryKey. </summary>
    public PrimaryKeyShape PrimaryKeyShape =>
        PrimaryKeyColumns.Count == 0 ? PrimaryKeyShape.None :
        PrimaryKeyColumns.Count > 1 ? PrimaryKeyShape.Composite :
        PrimaryKeyColumns[0].SqlType == SqlDbType.UniqueIdentifier ? PrimaryKeyShape.SingleUniqueIdentifier :
        PrimaryKeyColumns[0].SqlType is SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt or SqlDbType.TinyInt ? PrimaryKeyShape.SingleInt :
        PrimaryKeyShape.SingleOther;

    /// <summary> A NOT NULL text column called exactly "Name" plus a NOT NULL bit column called exactly
    /// "IsActive" -- the one shape test repeated identically across API_Crud.tt, CS_Entity.tt, CS_Repo.tt and
    /// the WinUI3 CRUD-screen family (each used to spell it out inline; canonical here so they can't drift
    /// apart). Such a table's repository is a NameActiveRepo, not a GenericRepo -- see
    /// TemplateConfig.RequiresNotNameActiveTable (Docs/specs.md section 5.3) for which templates that rules
    /// out and why. Exact-name match, not a SpecialLogicColumns.config pattern rule, matching how the
    /// existing inline checks were always written. </summary>
    public bool IsNameActiveTable =>
        Columns.Any(c => c.IsStringColumn && !c.IsNullable && c.Name == "Name") &&
        Columns.Any(c => c.SqlType == SqlDbType.Bit && !c.IsNullable && c.Name == "IsActive");

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

    public required List<ForeignKeyModel> ForeignKeys { get; init; }
    public bool HasAtLeastOneForeignKey => ForeignKeys.Count > 0;

    /// <summary> Other tables that have a foreign key pointing back at this one -- the mirror image of
    /// ForeignKeys. Always computed (not gated behind a template .tt.config flag), same as ForeignKeys. </summary>
    public required List<ChildForeignKeyModel> ChildForeignKeys { get; init; }
    public bool HasAtLeastOneChildForeignKey => ChildForeignKeys.Count > 0;

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
    private List<ColumnModel> JunctionCandidateColumns =>
        Columns.Where(c => !c.IsComputed && !c.IsAuditColumn && !(c.IsIdentity && c.IsPrimaryKey)).ToList();

    /// <summary> A many-to-many "junction"/"bridge" table: exactly two JunctionCandidateColumns, each
    /// individually covered by its own single-column foreign key (to the same parent table or two different
    /// ones -- a self-referencing many-to-many edge, e.g. a "follows" table between rows of the same Users
    /// table, counts too). A composite FK spanning both columns in one constraint does NOT count: that
    /// describes a table whose key duplicates a parent's own composite key, not a many-to-many association.
    /// Purely structural (no name-pattern guessing), unlike most SpecialLogicColumns.config categories. See
    /// CodeGenPossibilities\ShuttleControlJunctionTable in the research folder for the UI pattern this was
    /// added to support. </summary>
    public bool IsJunctionTable =>
        JunctionCandidateColumns.Count == 2 &&
        JunctionCandidateColumns.All(c => ForeignKeys.Any(fk =>
            fk.ReferencingColumns.Count == 1 && fk.ReferencingColumns[0].EqualsIgnoreCase(c.Name)));

    /// <summary> When IsJunctionTable, the two foreign keys that make up the many-to-many association (in
    /// JunctionCandidateColumns order) -- use this instead of ForeignKeys directly, since a junction table
    /// could rarely have another FK that isn't part of the association itself (e.g. a non-audit-named
    /// CreatedByUserId). Empty when IsJunctionTable is false. </summary>
    public List<ForeignKeyModel> JunctionForeignKeys =>
        !IsJunctionTable ? [] : JunctionCandidateColumns
            .Select(c => ForeignKeys.First(fk => fk.ReferencingColumns.Count == 1 && fk.ReferencingColumns[0].EqualsIgnoreCase(c.Name)))
            .ToList();

    // Special-logic, table-level (see Docs/specs.md section 7). Populated from SpecialLogicColumns.config.
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
