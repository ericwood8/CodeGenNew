using System.Data;

namespace CodeGenNew.Core;

/// <summary> One column of a TableModel, as read from the database plus classification computed by CodeGenNew.SchemaIntrospection. </summary>
public class ColumnModel
{
    #region Private Variables
    private bool? _isReservedWordName;
    private bool? _isCSharpReservedWordName;
    private bool? _isComputed;
    private bool? _isAuditColumn;
    private bool? _isIntegerColumn;
    private bool? _isNumericColumn;
    private bool? _isMoneyColumn;
    private bool? _isStringColumn;
    private bool? _isDateColumn;
    private bool? _isBooleanColumn;
    private CellKind? _cell;
    private string? _label;
    private NumericKind? _numericKind;
    private bool? _isPercentage;
    private string? _parameterName;
    private TextConversion? _conversion;
    private bool _conversionKnown;
    private bool? _isPgTextColumn;
    private bool? _isSpatialColumn;
    private bool? _isBitColumn;
    #endregion

    #region Private Strings
    private static readonly string[] LongTextNameEndings = ["Note", "Notes", "Comment", "Comments", "Remarks", "Memo"];
    private static readonly string[] CredentialNameEndings = ["Salt", "Secret", "ApiKey", "Token"];
    private static readonly string[] StampNameStarts = ["Create", "Modif", "Updated", "LastChang", "LastUpdat"];
    #endregion

    #region Private Enums
    private enum CellKind { Boolean, Date, DateTime, Money, Decimal, Plain }
    #endregion

    #region Private Misc
    private string CaptionOf()
    {
        string caption = Labels.NumberSign(Labels.Column(Name));
        if (!IsBooleanColumn)
            return caption;
        if (caption.StartsWith("Is ", StringComparison.OrdinalIgnoreCase) && caption.Length > 3)
            caption = caption[3..];
        return caption + "?";
    }

    // How a grid shows the value when the column is not a foreign key shown as the parent's name. The first that fits wins.
    private CellKind Cell => _cell ??=
        IsBooleanColumn ? CellKind.Boolean
        : TemplateHelpers.IsDate(this) ? (IsStampColumn && SqlType != SqlDbType.Date ? CellKind.DateTime : CellKind.Date)
        : IsMoneyAmount ? CellKind.Money
        : SqlType is SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney ? CellKind.Decimal
        : CellKind.Plain;
    #endregion

    /// <summary> The name generated code uses (a C# property, a TypeScript field). </summary>
    public required string Name { get; init; }

    /// <summary> The column's real name in the database when the project's naming style changed <see cref="Name"/> (customer_id -> CustomerId); null when they are the same. </summary>
    public string? DatabaseName { get; init; }

    /// <summary> The caption a screen shows for the column: <c>SY_</c> and a trailing <c>Id</c> dropped, then as words (<c>CustomerId</c> is "Customer"), "Number" as "#" (<c>CustomerItemNumber</c> is
    /// "Customer Item #"), and for a yes / no column a leading "Is" dropped and a question mark added (<c>IsTaxable</c> is "Taxable?"). </summary>
    public string Label => _label ??= CaptionOf();

    /// <summary> The name SQL text uses: the database's own name. </summary>
    public string DbName => DatabaseName ?? Name;

    /// <summary> The column's name quoted for the database it was read from. </summary>
    public required string QuotedName { get; init; }

    /// <summary> The database's name for the column is a reserved word of SQL. </summary>
    public bool IsReservedWordName => _isReservedWordName ??= DbName.IsSqlReservedWord();

    /// <summary> The generated name is a reserved word of C#. </summary>
    public bool IsCSharpReservedWordName => _isCSharpReservedWordName ??= Name.IsCSharpReservedWord();

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

    /// <summary> The database computes the column's value (it has a <see cref="ComputedDefinition"/>). </summary>
    public bool IsComputed => _isComputed ??= ComputedDefinition is not null;
    public string? ComputedDefinition { get; init; }

    /// <summary> Raw DEFAULT constraint text from the database, if any (e.g. "((0))", "(getdate())"). </summary>
    public string? DatabaseDefaultSql { get; init; }

    /// <summary>
    /// Best-effort C# literal translated from DatabaseDefaultSql, or (when there is no database
    /// default) the ported heuristic guess. Null when neither applies or the default couldn't be
    /// recognized. This is a rarely-used, low-priority convenience value -- never treat null as an error.
    /// </summary>
    public string? SuggestedCSharpDefaultValueLiteral { get; init; }

    // Type classification, worked out from SqlType.
    public bool IsIntegerColumn => _isIntegerColumn ??= SqlTypeKinds.IsInteger(SqlType);
    public bool IsNumericColumn => _isNumericColumn ??= SqlTypeKinds.IsNumeric(SqlType);
    public bool IsMoneyColumn => _isMoneyColumn ??= SqlTypeKinds.IsMoney(SqlType);
    public bool IsStringColumn => _isStringColumn ??= SqlTypeKinds.IsString(SqlType);
    public bool IsDateColumn => _isDateColumn ??= SqlTypeKinds.IsDate(SqlType);
    public bool IsBooleanColumn => _isBooleanColumn ??= SqlTypeKinds.IsBoolean(SqlType);

    /// <summary> A grid shows the value as Yes or No. </summary>
    public bool IsBooleanCell => Cell == CellKind.Boolean;
    /// <summary> A grid shows the value as a date. </summary>
    public bool IsDateCell => Cell == CellKind.Date;
    /// <summary> A grid shows the value as a day and the time of day (a date and time the system stamps). </summary>
    public bool IsDateTimeCell => Cell == CellKind.DateTime;
    /// <summary> A grid shows the value as an amount of money, with the currency. </summary>
    public bool IsMoneyCell => Cell == CellKind.Money;
    /// <summary> A grid shows the value as a decimal that is not money, with two decimals. </summary>
    public bool IsDecimalCell => Cell == CellKind.Decimal;
    /// <summary> A grid shows the value as it is (text, a whole number or anything else). </summary>
    public bool IsPlainCell => Cell == CellKind.Plain;

    /// <summary> A decimal, float or real column that is not money (a percentage, a rate, a weight): edited in a number box limited to what the column can hold. </summary>
    public bool IsDecimalBox => !IsCurrencyColumn && IsNumericColumn;

    /// <summary> A whole-number column (not a flag): edited in a number box limited to its range. </summary>
    public bool IsWholeNumberBox => IsIntegerColumn && !IsBooleanColumn;

    #region SqlDbType Properties
    /// <summary> What the database says the type is. Only the column reads it (the getter is not public): every question about the type is a property below. </summary>
    public required SqlDbType SqlType { internal get; init; }

    /// <summary> An int column (not bigint, smallint or tinyint: see <see cref="IsIntegerColumn"/> for every whole-number type). </summary>
    public bool IsInt32Column => SqlType == SqlDbType.Int;
    public bool IsGuidColumn => SqlType == SqlDbType.UniqueIdentifier;
    public bool IsBigIntColumn => SqlType == SqlDbType.BigInt;
    public bool IsSmallIntColumn => SqlType == SqlDbType.SmallInt;
    public bool IsTinyIntColumn => SqlType == SqlDbType.TinyInt;
    public bool IsDecimalColumn => SqlType == SqlDbType.Decimal;
    public bool IsFloatColumn => SqlType == SqlDbType.Float;
    public bool IsRealColumn => SqlType == SqlDbType.Real;
    public bool IsSmallMoneyColumn => SqlType == SqlDbType.SmallMoney;
    public bool IsDateOnlyColumn => SqlType == SqlDbType.Date; /// A date with no time of day.                                                               
    public bool IsDateTimeColumn => SqlType is SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime;
    public bool IsDateTime2Column => SqlType == SqlDbType.DateTime2;
    public bool IsSmallDateTimeColumn => SqlType == SqlDbType.SmallDateTime;
    public bool IsDateTimeOffsetColumn => SqlType == SqlDbType.DateTimeOffset;
    public bool IsTimeColumn => SqlType == SqlDbType.Time;
    /// <summary> A char, varchar, nchar or nvarchar column (not text, ntext or xml). </summary>
    public bool IsPlainTextColumn => SqlType is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar;
    public bool IsXmlColumn => SqlType == SqlDbType.Xml;
    /// <summary> Text kept in the Unicode types (nchar, nvarchar, ntext) or xml. </summary>
    public bool IsUnicodeTextColumn => SqlType is SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.NText or SqlDbType.Xml;
    /// <summary> A binary, varbinary or image column. </summary>
    public bool IsBinaryColumn => SqlType is SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image;
    /// <summary> A text, ntext or image column: the large-object types a table cannot copy, compare or keep history of. </summary>
    public bool IsLargeObjectColumn => IsLargeTextColumn || SqlType == SqlDbType.Image;
    public bool IsVariantColumn => SqlType == SqlDbType.Variant;
    /// <summary> A type with no text form to search or sort by (binary, xml, a row version, a user-defined or structured type, a variant). </summary>
    public bool IsOpaqueColumn => IsBinaryColumn || SqlType is SqlDbType.Xml or SqlDbType.Timestamp or SqlDbType.Udt or SqlDbType.Structured or SqlDbType.Variant;
    /// <summary> A text column's length in characters. The schema reader states the length of a Unicode column (<c>nchar</c>, <c>nvarchar</c>) in bytes, so it is halved; 0 when the length is not known,
    /// and a negative number for <c>varchar(max)</c> (callers test <c>&gt; 0</c>). </summary>
    public int CharacterLength => MaxLength is null ? 0 : SqlType is SqlDbType.NChar or SqlDbType.NVarChar ? MaxLength.Value / 2 : MaxLength.Value;
    /// <summary> A text or ntext column: the large-object text types a table cannot copy, compare or index like an ordinary string. </summary>
    public bool IsLargeTextColumn => SqlType is SqlDbType.Text or SqlDbType.NText;
    /// <summary> SQL Server's timestamp (rowversion): a value the database sets on every write, never copied or supplied. In PostgreSQL, MySQL and SQLite a timestamp is an ordinary date and time, which
    /// their schema readers map to a date type, so this is false for it. </summary>
    public bool IsRowVersionColumn => SqlType == System.Data.SqlDbType.Timestamp;
    /// <summary> The C# type without nullability: <c>int</c>, <c>string</c>, <c>byte[]</c>; <c>object</c> for a type with no mapping. </summary>
    public string CSharpBase() => SqlType switch
    {
        SqlDbType.Int => "int",
        SqlDbType.BigInt => "long",
        SqlDbType.SmallInt => "short",
        SqlDbType.TinyInt => "byte",
        SqlDbType.Bit => "bool",
        SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => "decimal",
        SqlDbType.Float => "double",
        SqlDbType.Real => "float",
        SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime => "DateTime",
        SqlDbType.DateTimeOffset => "DateTimeOffset",
        SqlDbType.Time => "TimeSpan",
        SqlDbType.UniqueIdentifier => "Guid",
        SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml => "string",
        SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Timestamp => "byte[]",
        _ => "object"
    };

    /// <summary> The TypeScript type of the value the API's JSON holds: <c>number</c> for every number (a decimal too), <c>boolean</c>, else <c>string</c> (a date is its ISO text). </summary>
    public string TypeScript() => SqlType switch
    {
        SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt or SqlDbType.TinyInt or SqlDbType.Decimal or SqlDbType.Money
            or SqlDbType.SmallMoney or SqlDbType.Float or SqlDbType.Real => "number",
        SqlDbType.Bit => "boolean",
        _ => "string"
    };
    /// <summary> The protobuf type: <c>int32</c>, <c>int64</c>, <c>bool</c>, <c>float</c>, <c>double</c>, <c>bytes</c>, <c>google.protobuf.Timestamp</c> for a date, and <c>string</c> for text, a decimal (protobuf has no decimal type), a time or a GUID. </summary>
    public string Proto() => SqlType switch
    {
        SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt => "int32",
        SqlDbType.BigInt => "int64",
        SqlDbType.Bit => "bool",
        SqlDbType.Float => "double",
        SqlDbType.Real => "float",
        SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset => "google.protobuf.Timestamp",
        SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Timestamp => "bytes",
        SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney or SqlDbType.Time or SqlDbType.UniqueIdentifier
            or SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml => "string",
        _ => "bytes"
    };

    /// <summary> The JSON Schema / OpenAPI <c>type</c> and <c>format</c> of the column's JSON value; the type is null for a database type with no JSON form. </summary>
    public (string? Type, string? Format) JsonSchema() => SqlType switch
    {
        SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt => ("integer", "int32"),
        SqlDbType.BigInt => ("integer", "int64"),
        SqlDbType.Bit => ("boolean", null),
        SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => ("number", null),
        SqlDbType.Float => ("number", "double"),
        SqlDbType.Real => ("number", "float"),
        SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset => ("string", "date-time"),
        SqlDbType.Time => ("string", null),
        SqlDbType.UniqueIdentifier => ("string", "uuid"),
        SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Timestamp => ("string", "byte"),
        SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml => ("string", null),
        _ => (null, null)
    };

    /// <summary> The T-SQL expression for "now" in this date column's own precision: SYSDATETIME() for a datetime2 (GETDATE() would keep three milliseconds), SYSDATETIMEOFFSET() for a datetimeoffset,
    /// GETDATE() for anything else. </summary>
    public string SqlServerNow => SqlType switch
    {
        SqlDbType.DateTime2 => "SYSDATETIME()",
        SqlDbType.DateTimeOffset => "SYSDATETIMEOFFSET()",
        _ => "GETDATE()"
    };
    #endregion

    /// <summary> A date or a date and time without an offset: the dates a form edits in a date box. </summary>
    public bool IsLocalDateColumn => IsDateOnlyColumn || IsDateTimeColumn;

    /// <summary> The column and <paramref name="other"/> have the same database type. </summary>
    public bool HasSameTypeAs(ColumnModel other) => SqlType == other.SqlType;


    /// <summary> The smallest and largest value the type of a whole-number column holds. </summary>
    public NumericRange TypeRange => NumericClassifier.TypeRange(SqlType);

    /// <summary> The values a text column may hold when the database lists them (a MySQL <c>enum('a','b')</c>); null for any other column. A generated form
    /// shows such a column as a drop-down of these values instead of a free text box. </summary>
    public List<string>? Choices { get; set; }
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

    /// <summary> The MySQL spatial type of the column (<c>point</c>, <c>polygon</c>, <c>geometry</c> ...); null for any other column. The column is text to the generator, holding well-known text
    /// (see <see cref="MySqlSpatialType"/>), and the SQL that reads and writes it converts. </summary>
    public string? DbSpatialType { get; init; }

    /// <summary> The spatial reference system id of a spatial column (0 when it has none). </summary>
    public int DbSrid { get; init; }

    /// <summary> The members of a MySQL <c>set('a','b')</c> column; null for any other column. The column holds some of them as text separated by commas. </summary>
    public List<string>? SetValues { get; init; }

    /// <summary> The width in bits of a MySQL <c>bit(n)</c> column of 2 to 64 bits, which is a whole number to the generator; null for any other column. </summary>
    public int? DbBitWidth { get; init; }

    /// <summary> The notation a value of this text column must be written in (an array, interval, range or shape of PostgreSQL, a MySQL set or spatial column), or null for plain text. </summary>
    /// <summary> The notation a CHECK constraint of the database puts on this text column (<c>CHECK (len(code) = 5)</c>, <c>CHECK (code LIKE 'AB%')</c>); null when it has none or the check is not that plain. </summary>
    public TextNotation? CheckNotation { get; init; }

    /// <summary> WriteFor a date column: the name of another date column of the table that a CHECK constraint says this one must not be earlier than (<c>CHECK (start_date &lt;= end_date)</c> on end_date gives start_date); null otherwise. </summary>
    public string? NotBeforeColumn { get; init; }

    public TextNotation? Notation => PgText is { } pg ? new TextNotation(pg.Pattern, pg.Description, pg.Sample)
        : MySqlSpatialType.FromName(DbSpatialType)?.Notation ?? (SetValues is { Count: > 0 } members ? MySqlSetNotation.Of(members) : null) ?? CheckNotation;

    /// <summary> The column's PostgreSQL type when it is not text, so the database has to be told what a text value is: an enum or one of the types of <see cref="DbTextType"/>. </summary>
    public string? DbCastType => DbTextType ?? DbEnumType;

    /// <summary> The limits a CHECK constraint of the database puts on this numeric column (<c>CHECK (credit_limit &gt;= 0)</c>); null when it has none or the check is not a plain range. The number
    /// boxes, the web inputs and the validation attributes use it with the limits the column's type and name already give. </summary>
    public CheckRange? Check { get; init; }

    /// <summary> What the database says about the column (SQL Server <c>MS_Description</c>, PostgreSQL <c>COMMENT ON COLUMN</c>, MySQL <c>COMMENT</c>); null when it says nothing. </summary>
    public string? Description { get; init; }

    /// <summary> What this integer column's name suggests about its values (a year, a month, a percentage, ...); None for any
    /// other column. See NumericClassifier, which holds the rule table. </summary>
    public NumericKind NumericKind => _numericKind ??= IsIntegerColumn ? NumericClassifier.Classify(Name) : NumericKind.None;

    /// <summary> The name says the column holds a percentage, whatever its type (a whole number or a decimal). </summary>
    public bool IsPercentage => _isPercentage ??= NumericClassifier.Classify(Name) == NumericKind.Percentage;

    /// <summary> A whole-number column whose values have limits beyond its type: its name suggests some (a month, a percentage) or a CHECK constraint of the database sets some. </summary>
    public bool HasNumericLimits => IsIntegerColumn && (NumericKind != NumericKind.None || Check is not null);

    /// <summary> The whole-number limits a number box for this column should enforce: what its name suggests (a year runs from <paramref name="minYear"/> to <paramref name="maxYear"/>, a month 1-12, a
    /// percentage 0-100, a count or sequence from 0), narrowed by a CHECK constraint, always inside the limits of its SQL type. Null for a column that is not a whole number. </summary>
    public NumericRange? RangeWith(long minYear, long maxYear)
    {
        if (!IsIntegerColumn)
            return null;
        var type = NumericClassifier.TypeRange(SqlType);
        (long min, long max) = NumericKind switch
        {
            NumericKind.Year => (minYear, maxYear),
            NumericKind.Month => (1L, 12L),
            NumericKind.DayOfMonth => (1L, 31L),
            NumericKind.Quarter => (1L, 4L),
            NumericKind.WeekNumber => (1L, 53L),
            NumericKind.Percentage => (0L, 100L),
            NumericKind.Count or NumericKind.Sequence => (0L, type.Max),
            _ => (type.Min, type.Max)
        };
        // what the database says in a CHECK constraint narrows it further (CHECK (rating BETWEEN 1 AND 5))
        if (Check is { } check)
        {
            long checkedMin = Math.Max(min, check.IntegerMin ?? min), checkedMax = Math.Min(max, check.IntegerMax ?? max);
            if (checkedMin <= checkedMax)
                (min, max) = (checkedMin, checkedMax);
        }
        return new NumericRange(Math.Max(min, type.Min), Math.Min(max, type.Max));
    }

    /// <summary> A money / smallmoney column, or a decimal column whose name says it holds money (see
    /// NumericClassifier.IsCurrencyName). A generated form edits it in a currency-formatted number box. </summary>
    public bool IsCurrencyColumn { get; init; }

    /// <summary> A money column that holds an amount with decimals (not a whole-number column the name merely calls money): shown with the currency symbol and edited in a money box. </summary>
    public bool IsMoneyAmount => IsCurrencyColumn && !IsIntegerColumn;

    /// <summary> A text column that can be very long: varchar/nvarchar(max), text/ntext, 500 or more characters, or a name that says it is a
    /// note (ends in Note, Notes, Comment, Comments, Remarks or Memo). Grids list these columns last so a long value cannot push the others off screen; a form puts them on its own "Notes" tab. </summary>
    public bool IsLongTextColumn =>
        IsStringColumn
        && (SqlType is SqlDbType.Text or SqlDbType.NText
            || MaxLength is -1
            || CharacterLength >= 500
            || Array.Exists(LongTextNameEndings, s => Name.EndsWithIgnoreCase(s)));

    /// <summary> One example value of the column as JSON text that the column accepts (a choice from its CHECK list, a number inside its range, text cut to its length). </summary>
    public string ValidExample(ProjectSettings project, string? table = null) => ApiSampleValues.Value(this, project, table);

    /// <summary> How the column's value is converted to and from the text the database holds (PostgreSQL arrays and intervals, MySQL spatial and BIT(64)); null for a column stored as it is. </summary>
    public TextConversion? Conversion
    {
        get
        {
            if (!_conversionKnown)
            {
                _conversion = PgText is { } pg ? new TextConversion($"{pg.Converter(IsNullable)}, PgText.Comparer", pg.Name)
                    : IsSpatialColumn && MySqlSpatialType.FromName(DbSpatialType) is not null ? new TextConversion($"MySqlSpatial.Converter({DbSrid}), MySqlSpatial.Comparer", null)   // the provider maps the bytes as they are; naming the spatial type confuses its type mapping
                    : DbBitWidth == 64 ? new TextConversion("new ValueConverter<decimal, ulong>(v => (ulong)v, v => v)", null)   // the driver reads BIT(64) as an unsigned 64-bit integer, which the entity holds as a decimal
                    : null;
                _conversionKnown = true;
            }
            return _conversion;
        }
    }

    /// <summary> The column holds a PostgreSQL array, interval, range or geometric value, which the generator handles as text in that type's notation. The Python and Rust code reads it as text and casts what it writes. </summary>
    public bool IsPgTextColumn => _isPgTextColumn ??= DbTextType is not null;

    /// <summary> The column holds a MySQL spatial value, which the generator handles as well-known text (the SQL converts it on the way in and out). </summary>
    public bool IsSpatialColumn => _isSpatialColumn ??= DbSpatialType is not null;

    /// <summary> The column is a MySQL <c>bit(n)</c> of 2 to 64 bits, which the generator handles as the whole number its bits make (<see cref="DbBitWidth"/> says how wide). </summary>
    public bool IsBitColumn => _isBitColumn ??= DbBitWidth is not null;

    /// <summary> The column's name as lower case words: "credit limit". </summary>
    public string Words => Labels.Words(Name).ToLowerInvariant();

    /// <summary> The column's name as words with its case kept: "Credit Limit". </summary>
    public string CaseWords => Labels.Words(Name);

    /// <summary> The same for a caption: a yes / no column drops its leading Is ("Taxable" for IsTaxable). </summary>
    public string FlagWords => Labels.Words(Name, IsBooleanColumn);

    /// <summary> A text column that holds a credential, judged by its name (a name with Password in it, or one that ends in Salt, Secret, ApiKey or Token): the generated screens show it neither in the grid nor in the form,
    /// and a row that is edited keeps the value it was read with. Keep such a table out of the API with <c>NoApiTables</c> when its rows must not be read at all. </summary>
    public bool IsCredentialColumn =>
        IsUserCredential || IsStringColumn && (Name.ContainsIgnoreCase("Password") || Array.Exists(CredentialNameEndings, s => Name.EndsWithIgnoreCase(s)));

    /// <summary> The project says this is a credential whatever it is called: the password hash column of the user table (<see cref="UserTable.MarkCredentials(TableModel, ProjectSettings)"/>, set before each template runs). </summary>
    public bool IsUserCredential { get; set; }

    /// <summary> Name-pattern match for create/modify/delete/activate/inactivate tracking columns
    /// (e.g. CreateDate, ModifiedBy, InactivatedDate). </summary>
    public bool IsAuditColumn => _isAuditColumn ??= Name.IsAuditColumn();

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

    /// <summary> A column of one of the five stamp categories of SpecialLogicColumns.config (CreateDate, CreateUser, ModifiedDate, ModifiedUser, LastChangedDate): who made the row and when it
    /// changed. This is the one list of them; code asks here instead of repeating the five flags. </summary>
    public bool IsStampColumn => IsCreateDateColumn || IsCreateUserColumn || IsModifiedDateColumn || IsModifiedUserColumn || IsLastChangedDateColumn;

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
    public string ParameterName => _parameterName ??= Name.ToSqlParameterName(SqlType);
}
