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

    /// <param name="qualifyTypes"> Write the parameter class with its namespace (a repository file has no using for it). </param>
    public SearchCall(TableModel model, string entity, IEnumerable<string> filterNames, bool qualifyTypes = false)
    {
        _prefix = qualifyTypes ? (model.Dialect == SqlDialect.PostgreSql ? "Npgsql." : model.Dialect == SqlDialect.MySql ? "MySql.Data.MySqlClient." : "Microsoft.Data.SqlClient.") : "";
        _postgres = model.Dialect == SqlDialect.PostgreSql;
        _mysql = model.Dialect == SqlDialect.MySql;
        _schema = model.SchemaName;
        _entity = entity;
        _filterArguments = filterNames.Select(n => "@p" + n).ToList();
    }

    /// <summary> The parameter class's namespace, for a using line (Microsoft.Data.SqlClient or Npgsql). </summary>
    public string ParameterNamespace => _postgres ? "Npgsql" : _mysql ? "MySql.Data.MySqlClient" : "Microsoft.Data.SqlClient";
    public string ParameterType => _postgres ? "NpgsqlParameter" : _mysql ? "MySqlParameter" : "SqlParameter";

    /// <summary> C# string literal CONTENT (quotes escaped) of the paged search statement. </summary>
    public string SearchSql
    {
        get
        {
            string args = string.Join(", ", _filterArguments.Append("@PageNumber").Append("@PageSize"));
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
            string args = string.Join(", ", _filterArguments);
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

    /// <summary> A C# expression creating a non-null integer parameter (@PageNumber, @PageSize). </summary>
    public string IntParameter(string name, string valueExpression) => _postgres
        ? $"new {_prefix}NpgsqlParameter(\"@{name}\", {valueExpression})"
        : _mysql ? $"new {_prefix}MySqlParameter(\"@{name}\", {valueExpression})"
        : $"new {_prefix}SqlParameter(\"@{name}\", {valueExpression})";
}
