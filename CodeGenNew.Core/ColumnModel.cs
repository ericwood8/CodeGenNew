using System.Data;

namespace CodeGenNew.Core;

/// <summary> One column of a TableModel, as read from the database plus classification computed by CodeGenNew.SchemaIntrospection. </summary>
public class ColumnModel
{
    public required string Name { get; init; }
    public required string QuotedName { get; init; }
    public bool IsReservedWordName { get; init; }
    public bool IsCSharpReservedWordName { get; init; }

    public required SqlDbType SqlType { get; init; }
    public required string SqlTypeDeclaration { get; init; }
    public int? MaxLength { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public bool IsNullable { get; init; }
    public int OrdinalPosition { get; init; }

    public bool IsIdentity { get; init; }
    public int? IdentitySeed { get; init; }
    public int? IdentityIncrement { get; init; }

    public bool IsPrimaryKey { get; init; }

    /// <summary> Takes part in a UNIQUE index or constraint other than the primary key (e.g. an account number). A copy
    /// of a row cannot keep such a value unchanged, which is what SP_Clone builds an override parameter for. </summary>
    public bool IsInUniqueIndex { get; init; }

    public bool IsComputed { get; init; }
    public string? ComputedDefinition { get; init; }

    /// <summary> Raw DEFAULT constraint text from the database, if any (e.g. "((0))", "(getdate())"). </summary>
    public string? DatabaseDefaultSql { get; init; }

    /// <summary>
    /// Best-effort C# literal translated from DatabaseDefaultSql, or (when there is no database
    /// default) the ported heuristic guess. Null when neither applies or the default couldn't be
    /// recognized. This is a rarely-used, low-priority convenience value -- never treat null as an error.
    /// </summary>
    public string? SuggestedCSharpDefaultValueLiteral { get; init; }

    // Type classification, grouped onto System.Data.SqlDbType.
    public bool IsIntegerColumn { get; init; }
    public bool IsNumericColumn { get; init; }
    public bool IsMoneyColumn { get; init; }
    public bool IsStringColumn { get; init; }
    public bool IsDateColumn { get; init; }
    public bool IsBooleanColumn { get; init; }

    /// <summary> Name-pattern match for create/modify/delete/activate/inactivate tracking columns
    /// (e.g. CreateDate, ModifiedBy, InactivatedDate). </summary>
    public bool IsAuditColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "CreateDateColumn" -- e.g. CreateDate, CreatedDate.
    /// Never touched by generated Update logic: excluded from parameters and never appears in a SET clause. </summary>
    public bool IsCreateDateColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "DisplayColumn" (e.g. ShortDescr, Name, AccountNumber): a
    /// column a Lookup shows so a person can recognize the row. The value is the index of the FIRST matching
    /// pattern, so a lower number is a better display/sort column; null = not a display column. Never set for
    /// text/ntext/xml/image/binary columns. </summary>
    public int? DisplayRank { get; init; }
    public bool IsDisplayColumn => DisplayRank.HasValue;

    /// <summary> Matches SpecialLogicColumns.config category "CreateUserColumn" -- e.g. CreateUser. A normal
    /// caller-supplied parameter on Insert and Save (who created the row), but excluded from Update parameters
    /// and from Save's UPDATE branch so the original creator is never overwritten. </summary>
    public bool IsCreateUserColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "ModifiedDateColumn" -- e.g. ModifiedDate, UpdatedDate.
    /// Excluded from Update parameters; generated Update logic sets it to GETDATE() unconditionally instead. </summary>
    public bool IsModifiedDateColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "ModifiedUserColumn" -- e.g. ModifiedBy, UpdatedBy.
    /// The Update-side mirror of IsCreateUserColumn: a normal caller-supplied parameter on Update (who is
    /// editing the row), excluded from Insert/Save's insert branch/Load/Clone entirely since a brand-new or
    /// freshly-cloned/seeded row has no prior editor. </summary>
    public bool IsModifiedUserColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "LastChangedDateColumn" -- e.g. LastDateChanged.
    /// A blend of create/modify: on Insert it's set alongside CreateDate; on Update it's set alongside
    /// ModifiedDate. Excluded from Update parameters; generated Update logic sets it to GETDATE()
    /// unconditionally, same as IsModifiedDateColumn. </summary>
    public bool IsLastChangedDateColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "InactiveReasonColumn" -- e.g.
    /// InactiveReasonNoteText. Excluded from Insert parameters and left NULL (see Docs/specs.md section 7). </summary>
    public bool IsInactiveReasonColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "AdminFlagColumn" -- e.g. IsAdmin. Excluded
    /// from Insert parameters and hardcoded to "not admin" (0/False), regardless of caller input, so a
    /// newly inserted row is never born with admin rights. </summary>
    public bool IsAdminFlagColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "FilePathColumn" -- e.g. LogoFile, PhotoPath,
    /// AttachmentFileName. Not yet consumed by any shipped template; forward-looking for a screen template
    /// where a matching column should render as a file picker + "open file" button instead of a plain
    /// text box. </summary>
    public bool IsFilePathColumn { get; init; }

    /// <summary> Hungarian-prefixed SQL parameter name, e.g. "@pstrDescription", "@plngID". See Docs/specs.md Appendix A. </summary>
    public required string ParameterName { get; init; }
}
