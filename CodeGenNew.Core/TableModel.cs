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

    public bool IsSelfReferencing(ForeignKeyModel fk) =>
        string.Equals(fk.ReferencedTable, TableName, StringComparison.OrdinalIgnoreCase);

    public bool IsForeignKeyMulti(ForeignKeyModel fk) =>
        ForeignKeys.Count(f => string.Equals(f.ReferencedTable, fk.ReferencedTable, StringComparison.OrdinalIgnoreCase)) > 1;

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
