using System.Data;

namespace CodeGenNew.Core;

/// <summary> One column of a TableModel, as read from the database plus classification computed by CodeGenNew.SchemaIntrospection. </summary>
public class ColumnModel
{
    /// <summary> The name generated code uses (a C# property, a TypeScript field). </summary>
    public required string Name { get; init; }

    /// <summary> The column's real name in the database when the project's naming style changed <see cref="Name"/> (customer_id -> CustomerId); null when they are the same. </summary>
    public string? DatabaseName { get; init; }

    /// <summary> The name SQL text uses: the database's own name. </summary>
    public string DbName => DatabaseName ?? Name;

    /// <summary> The column's name quoted for the database it was read from. </summary>
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

    /// <summary> An int column (not bigint, smallint or tinyint: see <see cref="IsIntegerColumn"/> for every whole-number type). </summary>
    public bool IsInt32Column => SqlType == System.Data.SqlDbType.Int;

    public bool IsGuidColumn => SqlType == System.Data.SqlDbType.UniqueIdentifier;

    /// <summary> A text column's length in characters. The schema reader states the length of a Unicode column (<c>nchar</c>, <c>nvarchar</c>) in bytes, so it is halved; 0 when the length is not known,
    /// and a negative number for <c>varchar(max)</c> (callers test <c>&gt; 0</c>). </summary>
    public int CharacterLength =>
        MaxLength is null ? 0 : SqlType is System.Data.SqlDbType.NChar or System.Data.SqlDbType.NVarChar ? MaxLength.Value / 2 : MaxLength.Value;

    /// <summary> A text or ntext column: the large-object text types a table cannot copy, compare or index like an ordinary string. </summary>
    public bool IsLargeTextColumn => SqlType is System.Data.SqlDbType.Text or System.Data.SqlDbType.NText;

    /// <summary> The values a text column may hold when the database lists them (a MySQL <c>enum('a','b')</c>); null for any other column. A generated form
    /// shows such a column as a drop-down of these values instead of a free text box. </summary>
    public List<string>? Choices { get; init; }
    public bool HasChoices => Choices is { Count: > 0 };

    /// <summary> The schema and name of the column's PostgreSQL enum type (<c>public.status</c>, from <c>CREATE TYPE status AS ENUM (...)</c>); null for any other
    /// column. The column is a text column to the generator, but PostgreSQL will not compare it with text (ILIKE, lower) or accept text for it unless told how: the
    /// search function casts the column to text, and the SP_EnumCasts script creates the assignment cast (text to the enum) that lets EF Core, which sends text, write it. </summary>
    public string? DbEnumType { get; init; }

    /// <summary> The PostgreSQL type of an array, interval, range or geometric column as a cast writes it (<c>int4[]</c>, <c>interval</c>, <c>int4range</c>, <c>point</c>); null for any other
    /// column. The column is a text column to the generator, holding the value in PostgreSQL's notation (see <see cref="PgTextType"/>): the raw SQL of the Python and Rust stacks reads it as
    /// text and casts what it writes, and the EF Core context converts it. </summary>
    public string? DbTextType { get; init; }

    /// <summary> The array, interval, range or geometric type behind <see cref="DbTextType"/>, or null. </summary>
    public PgTextType? PgText => PgTextType.FromName(DbTextType);

    /// <summary> The column's PostgreSQL type when it is not text, so the database has to be told what a text value is: an enum or one of the types of <see cref="DbTextType"/>. </summary>
    public string? DbCastType => DbTextType ?? DbEnumType;

    /// <summary> The limits a CHECK constraint of the database puts on this numeric column (<c>CHECK (credit_limit &gt;= 0)</c>); null when it has none or the check is not a plain range. The number
    /// boxes, the web inputs and the validation attributes use it with the limits the column's type and name already give. </summary>
    public CheckRange? Check { get; init; }

    /// <summary> What the database says about the column (SQL Server <c>MS_Description</c>, PostgreSQL <c>COMMENT ON COLUMN</c>, MySQL <c>COMMENT</c>); null when it says nothing. </summary>
    public string? Description { get; init; }

    /// <summary> What this integer column's name suggests about its values (a year, a month, a percentage, ...); None for any
    /// other column. See NumericClassifier; ProjectSettings.RangeFor turns it into limits. </summary>
    public NumericKind NumericKind { get; init; }

    /// <summary> A money / smallmoney column, or a decimal column whose name says it holds money (see
    /// NumericClassifier.IsCurrencyName). A generated form edits it in a currency-formatted number box. </summary>
    public bool IsCurrencyColumn { get; init; }

    /// <summary> A money column that holds an amount with decimals (not a whole-number column the name merely calls money): shown with the currency symbol and edited in a money box. </summary>
    public bool IsMoneyAmount => IsCurrencyColumn && !IsIntegerColumn;

    /// <summary> A text column that can be very long: varchar/nvarchar(max), text/ntext, 500 or more characters, or a name that says it is a
    /// note (ends in Note, Notes, Comment, Comments, Remarks or Memo). Grids list these columns last so a long value cannot push the others off screen; a form puts them on its own "Notes" tab. </summary>
    public bool IsLongTextColumn =>
        IsStringColumn
        && (SqlType is System.Data.SqlDbType.Text or System.Data.SqlDbType.NText
            || MaxLength is -1
            || CharacterLength >= 500
            || Array.Exists(LongTextNameEndings, s => Name.EndsWithIgnoreCase(s)));

    private static readonly string[] LongTextNameEndings = ["Note", "Notes", "Comment", "Comments", "Remarks", "Memo"];

    /// <summary> A text column that holds a credential, judged by its name (a name with Password in it, or one that ends in Salt, Secret, ApiKey or Token): the generated screens show it neither in the grid nor in the form,
    /// and a row that is edited keeps the value it was read with. Keep such a table out of the API with <c>NoApiTables</c> when its rows must not be read at all. </summary>
    public bool IsCredentialColumn =>
        IsUserCredential || IsStringColumn && (Name.ContainsIgnoreCase("Password") || Array.Exists(CredentialNameEndings, s => Name.EndsWithIgnoreCase(s)));

    /// <summary> The project says this is a credential whatever it is called: the password hash column of the user table (<see cref="UserTable.MarkCredentials(TableModel, ProjectSettings)"/>, set before each template runs). </summary>
    public bool IsUserCredential { get; set; }

    private static readonly string[] CredentialNameEndings = ["Salt", "Secret", "ApiKey", "Token"];

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

    /// <summary> SQL Server's timestamp (rowversion): a value the database sets on every write, never copied or supplied. In PostgreSQL, MySQL and SQLite a timestamp is an ordinary date and time, which
    /// their schema readers map to a date type, so this is false for it. </summary>
    public bool IsRowVersionColumn => SqlType == System.Data.SqlDbType.Timestamp;

    /// <summary> A column of one of the five stamp categories of SpecialLogicColumns.config (CreateDate, CreateUser, ModifiedDate, ModifiedUser, LastChangedDate): who made the row and when it
    /// changed. This is the one list of them; code asks here instead of repeating the five flags. </summary>
    public bool IsStampColumn => IsCreateDateColumn || IsCreateUserColumn || IsModifiedDateColumn || IsModifiedUserColumn || IsLastChangedDateColumn;

    /// <summary> The T-SQL expression for "now" in this date column's own precision: SYSDATETIME() for a datetime2 (GETDATE() would keep three milliseconds), SYSDATETIMEOFFSET() for a datetimeoffset,
    /// GETDATE() for anything else. </summary>
    public string SqlServerNow => SqlType switch
    {
        System.Data.SqlDbType.DateTime2 => "SYSDATETIME()",
        System.Data.SqlDbType.DateTimeOffset => "SYSDATETIMEOFFSET()",
        _ => "GETDATE()"
    };

    /// <summary> A date the routines write themselves on an insert (CreateDate, LastChangedDate): never a parameter, set to the current time. </summary>
    public bool IsInsertStampColumn => IsCreateDateColumn || IsLastChangedDateColumn;

    /// <summary> A date column of the audit trail (CreateDate, ModifiedDate, LastChangedDate): the routines set it, a caller never supplies it. </summary>
    public bool IsAuditDateColumn => IsCreateDateColumn || IsModifiedDateColumn || IsLastChangedDateColumn;

    /// <summary> A stamp column an update never takes from the caller: the creation date and user are kept, the modified and last-changed dates are set by the routine. (The modified-by user is the caller's.) </summary>
    public bool IsLeftOutOfUpdate => IsStampColumn && !IsModifiedUserColumn;

    /// <summary> Who made the row and when it last changed (CreateDate, CreateUser, ModifiedDate, ModifiedBy, LastDateChanged ...): the system writes them, so a form shows them as text
    /// (a label with the value) and never as a box a person can change. </summary>
    public bool IsSystemStampColumn =>
        IsStampColumn
        || (IsAuditColumn && Array.Exists(StampNameStarts, s => Name.StartsWithIgnoreCase(s)));

    private static readonly string[] StampNameStarts = ["Create", "Modif", "Updated", "LastChang", "LastUpdat"];

    /// <summary> Matches SpecialLogicColumns.config category "InactiveReasonColumn" -- e.g.
    /// InactiveReasonNoteText. Excluded from Insert parameters and left NULL (see Docs/Reference.md section 5). </summary>
    public bool IsInactiveReasonColumn { get; init; }

    /// <summary> The name says the flag is negative: an "Inactive" column (IsInactive, InactiveFlag) is set to 1 to turn a row off, where an "Active" column is set to 1 to keep it on. </summary>
    public bool IsInactive => Name.ContainsIgnoreCase("Inactive");

    /// <summary> Matches SpecialLogicColumns.config category "AdminFlagColumn" -- e.g. IsAdmin. Excluded
    /// from Insert parameters and hardcoded to "not admin" (0/False), regardless of caller input, so a
    /// newly inserted row is never born with admin rights. </summary>
    public bool IsAdminFlagColumn { get; init; }

    /// <summary> Matches SpecialLogicColumns.config category "FilePathColumn" -- e.g. LogoFile, PhotoPath,
    /// AttachmentFileName. Not yet consumed by any shipped template; forward-looking for a screen template
    /// where a matching column should render as a file picker + "open file" button instead of a plain
    /// text box. </summary>
    public bool IsFilePathColumn { get; init; }

    /// <summary> Hungarian-prefixed SQL parameter name, e.g. "@pstrDescription", "@plngID". See Docs/Reference.md section 7. </summary>
    public required string ParameterName { get; init; }
}
