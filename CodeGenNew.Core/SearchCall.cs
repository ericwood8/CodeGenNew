namespace CodeGenNew.Core;

/// <summary> How generated C# calls a table's search / count routine, which differs by database: SQL Server runs the
/// <c>EXEC [schema].[Table_Search] @p...</c> stored procedures that SP_Search writes; PostgreSQL has functions, called with
/// <c>SELECT * FROM "schema"."Table_Search"(@p...)</c> and <c>SELECT "schema"."Table_SearchCount"(@p...) AS "Value"</c>
/// (SqlQueryRaw reads a scalar from a column named Value). Used by CS_Repo and API_Search so the two stay identical. </summary>
public sealed class SearchCall
{
    private readonly bool _postgres;
    private readonly bool _mysql;
    private readonly string _prefix;
    private readonly string _schema;
    private readonly string _entity;
    private readonly List<string> _filterArguments;
    private readonly List<string> _keyArguments;

    /// <param name="qualifyTypes"> Write the parameter class with its namespace (a repository file has no using for it). </param>
    public SearchCall(TableModel model, string entity, IEnumerable<string> filterNames, bool qualifyTypes = false)
    {
        _prefix = qualifyTypes ? (model.Dialect == SqlDialect.PostgreSql ? "Npgsql." : model.Dialect == SqlDialect.MySql ? "MySql.Data.MySqlClient." : "Microsoft.Data.SqlClient.") : "";
        _postgres = model.Dialect == SqlDialect.PostgreSql;
        _mysql = model.Dialect == SqlDialect.MySql;
        _schema = model.SchemaName;
        _entity = entity;
        _filterArguments = filterNames.Select(n => "@p" + n).ToList();
        _keyArguments = model.SearchKeyColumns.Select(c => "@p" + c.Name).ToList();
    }

    /// <summary> The parameter class's namespace, for a using line (Microsoft.Data.SqlClient or Npgsql). </summary>
    public string ParameterNamespace => _postgres ? "Npgsql" : _mysql ? "MySql.Data.MySqlClient" : "Microsoft.Data.SqlClient";
    public string ParameterType => _postgres ? "NpgsqlParameter" : _mysql ? "MySqlParameter" : "SqlParameter";

    /// <summary> C# string literal CONTENT (quotes escaped) of the paged search statement. </summary>
    public string SearchSql
    {
        get
        {
            string args = string.Join(", ", _filterArguments.Append("@PageNumber").Append("@PageSize").Append("@SortColumn").Append("@SortDescending").Concat(_keyArguments));
            return _postgres ? $"SELECT * FROM \\\"{_schema}\\\".\\\"{_entity}_Search\\\"({args})"
                : _mysql ? $"CALL `{_entity}_Search`({args})"
                : $"EXEC [{_schema}].[{_entity}_Search] {args}";
        }
    }

    /// <summary> C# string literal CONTENT of the count statement. </summary>
    public string CountSql
    {
        get
        {
            string args = string.Join(", ", _filterArguments.Concat(_keyArguments));
            return _postgres
                ? $"SELECT \\\"{_schema}\\\".\\\"{_entity}_SearchCount\\\"({args}) AS \\\"Value\\\""
                : _mysql ? $"CALL `{_entity}_SearchCount`({args})"
                : $"EXEC [{_schema}].[{_entity}_SearchCount]{(args.Length > 0 ? " " + args : "")}";
        }
    }

    /// <summary> A C# expression creating a text filter parameter; <paramref name="valueExpression"/> may be null (sent as DBNull). </summary>
    public string TextParameter(string name, string valueExpression) => _postgres
        ? $"new {_prefix}NpgsqlParameter(\"@p{name}\", NpgsqlTypes.NpgsqlDbType.Text) {{ Value = (object?){valueExpression} ?? DBNull.Value }}"
        : _mysql ? $"new {_prefix}MySqlParameter(\"@p{name}\", (object?){valueExpression} ?? DBNull.Value)"
        : $"new {_prefix}SqlParameter(\"@p{name}\", (object?){valueExpression} ?? DBNull.Value)";

    /// <summary> A C# expression creating a key filter parameter for <paramref name="column"/> (a foreign key); <paramref name="valueExpression"/> is a nullable number or Guid, or text, and is sent as DBNull
    /// when null (or, for text, blank). </summary>
    public string KeyParameter(ColumnModel column, string valueExpression)
    {
        string name = "@p" + column.Name;
        if (column.CSharpBase() == "string")
            valueExpression = $"(string.IsNullOrEmpty({valueExpression}) ? null : {valueExpression})";
        if (_postgres)
        {
            string type = column.CSharpBase() switch { "long" => "Bigint", "int" => "Integer", "string" => "Text", "Guid" => "Uuid", _ => "Smallint" };
            return $"new {_prefix}NpgsqlParameter(\"{name}\", NpgsqlTypes.NpgsqlDbType.{type}) {{ Value = (object?){valueExpression} ?? DBNull.Value }}";
        }
        return _mysql ? $"new {_prefix}MySqlParameter(\"{name}\", (object?){valueExpression} ?? DBNull.Value)"
            : $"new {_prefix}SqlParameter(\"{name}\", (object?){valueExpression} ?? DBNull.Value)";
    }

    /// <summary> The grid's chosen sort column (a column name, or null for the default order); the routine compares it with its own list of columns (SearchSort). </summary>
    public string SortColumnParameter(string valueExpression) => _postgres
        ? $"new {_prefix}NpgsqlParameter(\"@SortColumn\", NpgsqlTypes.NpgsqlDbType.Text) {{ Value = (object?){valueExpression} ?? DBNull.Value }}"
        : _mysql ? $"new {_prefix}MySqlParameter(\"@SortColumn\", (object?){valueExpression} ?? DBNull.Value)"
        : $"new {_prefix}SqlParameter(\"@SortColumn\", (object?){valueExpression} ?? DBNull.Value)";

    /// <summary> The sort direction: true for descending. </summary>
    public string SortDescendingParameter(string valueExpression) => _postgres
        ? $"new {_prefix}NpgsqlParameter(\"@SortDescending\", {valueExpression})"
        : _mysql ? $"new {_prefix}MySqlParameter(\"@SortDescending\", {valueExpression})"
        : $"new {_prefix}SqlParameter(\"@SortDescending\", {valueExpression})";

    /// <summary> A C# expression creating a non-null integer parameter (@PageNumber, @PageSize). </summary>
    public string IntParameter(string name, string valueExpression) => _postgres
        ? $"new {_prefix}NpgsqlParameter(\"@{name}\", {valueExpression})"
        : _mysql ? $"new {_prefix}MySqlParameter(\"@{name}\", {valueExpression})"
        : $"new {_prefix}SqlParameter(\"@{name}\", {valueExpression})";
}
