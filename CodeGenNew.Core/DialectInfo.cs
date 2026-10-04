namespace CodeGenNew.Core;

/// <summary> The units a date can be grouped into, for a trend over time. </summary>
public enum DateBucket
{
    Day,
    Month,
    Year
}

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

    /// <summary> The Nth (1-based) positional placeholder: <c>$1</c> for PostgreSQL, <c>?</c> for MySQL and SQLite, <c>@p1</c> for SQL Server. For a driver, such as Rust's sqlx, that binds by position. </summary>
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
        SqlDialect.MySql => $"{columnExpression} LIKE CONCAT('%', {parameterExpression}, '%') ESCAPE '{LikeEscapeCharacter}'",
        _ => $"{columnExpression} LIKE '%' || {parameterExpression} || '%' ESCAPE '{LikeEscapeCharacter}'"
    };

    /// <summary> The escape character a LIKE pattern built with <see cref="EscapeLike"/> uses. </summary>
    public const char LikeEscapeCharacter = '\\';

    /// <summary> Puts the escape character before every character that is a wildcard in a LIKE pattern (<c>%</c>, <c>_</c>, SQL Server's <c>[</c>) and before the escape character itself, so a search
    /// for <c>50%</c> matches those three characters and not "50 followed by anything". </summary>
    public static string EscapeLike(string value) => value
        .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

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

    private static string BucketName(DateBucket bucket) => bucket switch { DateBucket.Day => "day", DateBucket.Month => "month", _ => "year" };

    /// <summary> A boolean as a literal: <c>1</c> or <c>0</c> where there is no boolean type (SQL Server, MySQL, SQLite), <c>TRUE</c> or <c>FALSE</c> for PostgreSQL. </summary>
    public string BooleanLiteral(bool value) => Dialect == SqlDialect.PostgreSql ? (value ? "TRUE" : "FALSE") : (value ? "1" : "0");

    /// <summary> The current time as the database knows it. </summary>
    public string CurrentTimestamp => Dialect switch
    {
        SqlDialect.SqlServer => "SYSDATETIME()",
        SqlDialect.MySql => "NOW()",
        SqlDialect.Sqlite => "datetime('now')",
        _ => "now()"
    };
}
