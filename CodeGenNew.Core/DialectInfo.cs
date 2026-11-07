namespace CodeGenNew.Core;

/// <summary> The units a date can be grouped into, for a trend over time. </summary>
public enum DateBucket { Day, Month, Year }

/// <summary> What differs between the databases when a template writes SQL text itself (a repository that carries its own statements, a query for a dashboard) and not
/// through the provider's own routines. Every member answers one question, so a template stays free of per-database branches. </summary>
public sealed class DialectInfo
{
    private DialectInfo(SqlDialect dialect, string name, bool supportsRoutines, bool hasSchemas)
    {
        Dialect = dialect;
        Name = name;
        SupportsRoutines = supportsRoutines;
        HasSchemas = hasSchemas;
    }

    public static DialectInfo For(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => new(dialect, "PostgreSQL", supportsRoutines: true, hasSchemas: true),
        SqlDialect.MySql => new(dialect, "MySQL", supportsRoutines: true, hasSchemas: false),
        SqlDialect.Sqlite => new(dialect, "SQLite", supportsRoutines: false, hasSchemas: false),
        _ => new(dialect, "SQL Server", supportsRoutines: true, hasSchemas: true)
    };

    public SqlDialect Dialect { get; }
    public bool IsPostgreSql => Dialect == SqlDialect.PostgreSql;
    public string Name { get; }

    /// <summary> False for SQLite: it has no stored procedures or functions, so everything the routine templates write has to be done in code (see <see cref="AccessMode"/>). </summary>
    public bool SupportsRoutines { get; }

    /// <summary> False where the database and the schema are one thing (MySQL) or there is no schema at all (SQLite): a table name is not prefixed. </summary>
    public bool HasSchemas { get; }

    /// <summary> An identifier in this database's quotes: <c>[Name]</c>, <c>"Name"</c> or <c>`Name`</c>, with a quote inside the name doubled. </summary>
    public string Quote(string identifier) => Dialect switch
    {
        SqlDialect.SqlServer => "[" + identifier.Replace("]", "]]") + "]",
        SqlDialect.MySql => "`" + identifier.Replace("`", "``") + "`",
        _ => "\"" + identifier.Replace("\"", "\"\"") + "\""
    };

    /// <summary> A table as a statement names it: schema and table where there are schemas, else the table alone. </summary>
    public string QuoteTable(string schema, string table) => HasSchemas ? Quote(schema) + "." + Quote(table) : Quote(table);

    /// <summary> The Nth (1-based) positional placeholder: <c>$1</c> for PostgreSQL, <c>?</c> for MySQL and SQLite, <c>@p1</c> for SQL Server. WriteFor a driver, such as Rust's sqlx, that binds by position. </summary>
    public string Placeholder(int index) => Dialect switch
    {
        SqlDialect.PostgreSql => "$" + index,
        SqlDialect.SqlServer => "@p" + index,
        _ => "?"
    };

    /// <summary> The paging clause for a statement that already has an ORDER BY. </summary>
    public string Paging(string offsetExpression, string limitExpression) => Dialect == SqlDialect.SqlServer
        ? $"OFFSET {offsetExpression} ROWS FETCH NEXT {limitExpression} ROWS ONLY"
        : $"LIMIT {limitExpression} OFFSET {offsetExpression}";

    /// <summary> The text of "the column contains the value, ignoring case", the value being a bound parameter already escaped with <see cref="EscapeLike"/>
    /// (the statement says <c>ESCAPE</c> with <see cref="LikeEscapeCharacter"/>). SQL Server, MySQL and SQLite compare case-insensitively by default for ordinary text; PostgreSQL needs ILIKE. </summary>
    public string ContainsCondition(string columnExpression, string parameterExpression) => Dialect switch
    {
        SqlDialect.PostgreSql => $"{columnExpression} ILIKE '%' || {parameterExpression} || '%' ESCAPE '{LikeEscapeCharacter}'",
        SqlDialect.SqlServer => $"{columnExpression} LIKE '%' + {parameterExpression} + '%' ESCAPE '{LikeEscapeCharacter}'",
        SqlDialect.MySql => $"{columnExpression} LIKE CONCAT('%', {parameterExpression}, '%') ESCAPE '{LikeEscapeCharacter}{LikeEscapeCharacter}'",   // a backslash is itself an escape in a MySQL string
        _ => $"{columnExpression} LIKE '%' || {parameterExpression} || '%' ESCAPE '{LikeEscapeCharacter}'"
    };

    /// <summary> <see cref="ContainsCondition"/> split around the parameter: the text before it and the text after it, for code that adds the bound value itself (Rust's sqlx query builder
    /// writes the placeholder). </summary>
    public (string Before, string After) ContainsParts(string columnExpression)
    {
        const string marker = "\u0001";
        string text = ContainsCondition(columnExpression, marker);
        int at = text.IndexOf(marker, StringComparison.Ordinal);
        return (text[..at], text[(at + marker.Length)..]);
    }

    /// <summary> The escape character a LIKE pattern built with <see cref="EscapeLike"/> uses. </summary>
    public const char LikeEscapeCharacter = '\\';

    /// <summary> Puts the escape character before every character that is a wildcard in a LIKE pattern (<c>%</c>, <c>_</c>, SQL Server's <c>[</c>) and before the escape character itself, so a search
    /// for <c>50%</c> matches those three characters and not "50 followed by anything". </summary>
    public static string EscapeLike(string value) => value
        .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    /// <summary> The SQL that does <see cref="EscapeLike"/> inside a routine, for a value the routine received as a parameter: wrap it in the result before <see cref="ContainsCondition"/>
    /// so a search for <c>50%</c> matches those three characters. SQL Server also escapes <c>[</c>. MySQL reads a backslash in a string literal as an escape, so each one is written twice. </summary>
    public string EscapeLikeSql(string valueExpression) => Dialect switch
    {
        SqlDialect.SqlServer => $"REPLACE(REPLACE(REPLACE(REPLACE({valueExpression}, '\\', '\\\\'), '%', '\\%'), '_', '\\_'), '[', '\\[')",
        SqlDialect.MySql => $"REPLACE(REPLACE(REPLACE({valueExpression}, '\\\\', '\\\\\\\\'), '%', '\\\\%'), '_', '\\\\_')",
        _ => $"REPLACE(REPLACE(REPLACE({valueExpression}, '\\', '\\\\'), '%', '\\%'), '_', '\\_')"
    };

    /// <summary> An expression for the start of the day, month or year the date expression falls in, as a date (a text in ISO form for SQLite and MySQL, which group by it). </summary>
    public string DateBucketExpression(string dateExpression, DateBucket bucket) => Dialect switch
    {
        SqlDialect.PostgreSql => $"date_trunc('{BucketName(bucket)}', {dateExpression})::date",
        SqlDialect.MySql => bucket switch
        {
            DateBucket.Day => $"DATE({dateExpression})",
            DateBucket.Month => $"DATE_FORMAT({dateExpression}, '%Y-%m-01')",
            _ => $"DATE_FORMAT({dateExpression}, '%Y-01-01')"
        },
        SqlDialect.Sqlite => bucket switch
        {
            DateBucket.Day => $"date({dateExpression})",
            DateBucket.Month => $"strftime('%Y-%m-01', {dateExpression})",
            _ => $"strftime('%Y-01-01', {dateExpression})"
        },
        _ => bucket switch
        {
            DateBucket.Day => $"CAST({dateExpression} AS date)",
            DateBucket.Month => $"DATEFROMPARTS(YEAR({dateExpression}), MONTH({dateExpression}), 1)",
            _ => $"DATEFROMPARTS(YEAR({dateExpression}), 1, 1)"
        }
    };

    /// <summary> The expression as text, for a label. A date comes out in ISO form through <see cref="DateText"/>, not through this. </summary>
    public string ToText(string expression) => Dialect switch
    {
        SqlDialect.SqlServer => $"CAST({expression} AS nvarchar(255))",
        SqlDialect.MySql => $"CAST({expression} AS CHAR)",
        SqlDialect.Sqlite => $"CAST({expression} AS TEXT)",
        _ => $"CAST({expression} AS text)"
    };

    /// <summary> The date expression as <c>yyyy-MM-dd</c> text, whatever the database's own date format is. </summary>
    public string DateText(string expression) => Dialect switch
    {
        SqlDialect.SqlServer => $"CONVERT(nvarchar(10), {expression}, 23)",
        SqlDialect.MySql => $"DATE_FORMAT({expression}, '%Y-%m-%d')",
        SqlDialect.Sqlite => $"date({expression})",
        _ => $"to_char({expression}, 'YYYY-MM-DD')"
    };

    /// <summary> The expression as a double-precision number, so every database hands the same type to the reader (a count is a bigint, a sum a decimal, PostgreSQL's money a type of its own). </summary>
    public string ToDouble(string expression) => Dialect switch
    {
        SqlDialect.SqlServer => $"CAST({expression} AS float)",
        SqlDialect.MySql => $"CAST({expression} AS DOUBLE)",
        SqlDialect.Sqlite => $"CAST({expression} AS REAL)",
        _ => $"CAST({expression} AS double precision)"
    };

    /// <summary> The date expression moved back by <paramref name="count"/> days, months or years. </summary>
    public string DateBefore(string dateExpression, DateBucket unit, int count) => Dialect switch
    {
        SqlDialect.PostgreSql => $"({dateExpression} - interval '{count} {BucketName(unit)}s')",
        SqlDialect.MySql => $"DATE_SUB({dateExpression}, INTERVAL {count} {BucketName(unit).ToUpperInvariant()})",
        SqlDialect.Sqlite => $"datetime({dateExpression}, '-{count} {BucketName(unit)}s')",
        _ => $"DATEADD({BucketName(unit)}, -{count}, {dateExpression})"
    };

    private static string BucketName(DateBucket bucket) => bucket switch { DateBucket.Day => "day", DateBucket.Month => "month", _ => "year" };

    /// <summary> A boolean as a literal: <c>1</c> or <c>0</c> where there is no boolean type (SQL Server, MySQL, SQLite), <c>TRUE</c> or <c>FALSE</c> for PostgreSQL. </summary>
    public string BooleanLiteral(bool value) => Dialect == SqlDialect.PostgreSql ? (value ? "TRUE" : "FALSE") : (value ? "1" : "0");

    /// <summary> A boolean in lower case as JSON and PostgreSQL write it (<c>true</c>), or <c>1</c> and <c>0</c> where there is no boolean type. </summary>
    public string BooleanText(bool value) => Dialect == SqlDialect.PostgreSql ? (value ? "true" : "false") : (value ? "1" : "0");

    /// <summary> The schema a table is in when the project does not name one: <c>dbo</c> for SQL Server, <c>public</c> otherwise. </summary>
    public string DefaultSchema => Dialect == SqlDialect.SqlServer ? "dbo" : "public";

    /// <summary> True when the database has schemas and the table's is not the default one, so generated code must name it. </summary>
    public bool NamesSchema(string schema) => HasSchemas && !schema.EqualsIgnoreCase(DefaultSchema);

    /// <summary> A positional placeholder that holds a date and time: PostgreSQL will not take the driver's text for a <c>timestamp</c> without a cast. </summary>
    public string TimestampPlaceholder(int index) => Dialect == SqlDialect.PostgreSql ? $"CAST({Placeholder(index)} AS TIMESTAMP)" : Placeholder(index);

    /// <summary> False for MySQL: an INSERT cannot return the row it wrote, so the key is read back with a second statement. </summary>
    public bool SupportsReturning => Dialect != SqlDialect.MySql;

    /// <summary> False for SQL Server: the generated Python tests seed and read their own database, and run over SQLite, PostgreSQL or MySQL only. </summary>
    public bool RunsPythonTests => Dialect != SqlDialect.SqlServer;

    /// <summary> False for SQL Server: <c>CREATE TABLE IF NOT EXISTS</c> is not accepted, so a script tests <c>OBJECT_ID</c> first. </summary>
    public bool SupportsCreateIfNotExists => Dialect != SqlDialect.SqlServer;

    /// <summary> True for SQL Server: a row with a value for an identity column is refused unless identity insert is switched on, so a copy that keeps the key cannot go into one. </summary>
    public bool RefusesKeyForIdentityColumn => Dialect == SqlDialect.SqlServer;

    /// <summary> True for SQLite: it types a value by what it holds, so an empty sum has to be written 0.0 to reach a reader as a real. </summary>
    public bool NeedsRealZero => Dialect == SqlDialect.Sqlite;

    /// <summary> True for PostgreSQL: the money type has no AVG and no cast to double, so an aggregate casts it to numeric first. </summary>
    public bool MoneyNeedsNumericCast => Dialect == SqlDialect.PostgreSql;

    /// <summary> The namespace prefix of the ADO.NET driver's classes (<c>Npgsql.</c>), for generated code that names a driver type. </summary>
    public string DriverPrefix => Dialect switch { SqlDialect.PostgreSql => "Npgsql.", SqlDialect.MySql => "MySqlConnector.", _ => "Microsoft.Data.SqlClient." };

    /// <summary> The column type of a date and time without a zone: <c>datetime2</c>, <c>datetime</c> or <c>timestamp</c>. </summary>
    public string DateTimeType => Dialect switch { SqlDialect.SqlServer => "datetime2", SqlDialect.MySql => "datetime", _ => "timestamp" };

    /// <summary> MySQL: well-known text to a spatial value in the column's reference system, longitude first (blank is NULL). MariaDB has no axis-order option and keeps the order it is given,
    /// so its function takes the text and the system only. </summary>
    public string SpatialFromText(string textExpression, int srid, bool mariaDb) => mariaDb
        ? $"ST_GeomFromText(NULLIF({textExpression}, ''), {srid})"
        : $"ST_GeomFromText(NULLIF({textExpression}, ''), {srid}, 'axis-order=long-lat')";

    /// <summary> MySQL: a spatial column as well-known text, longitude first; MariaDB writes it that way already. </summary>
    public string SpatialAsText(string columnExpression, bool mariaDb) => mariaDb ? $"ST_AsText({columnExpression})" : $"ST_AsText({columnExpression}, 'axis-order=long-lat')";

    /// <summary> The current time as the database knows it. </summary>
    public string CurrentTimestamp => Dialect switch
    {
        SqlDialect.SqlServer => "SYSDATETIME()",
        SqlDialect.MySql => "NOW()",
        SqlDialect.Sqlite => "datetime('now')",
        _ => "now()"
    };
}
