namespace CodeGenNew.Core;

/// <summary> How generated C# calls a junction table's List / Link / Unlink routines (SP_Junction), which differs by database: SQL Server
/// runs <c>EXEC [schema].[Table_List] @Anchor...</c>, PostgreSQL calls its functions (<c>SELECT * FROM "schema"."Table_List"(...)</c>,
/// <c>SELECT "schema"."Table_Link"(...)</c>) and MySQL its procedures (<c>CALL `Table_List`(...)</c>). Used by API_Junction and
/// WinUI3_JunctionEditor so the two stay identical. Every string is C# string-literal CONTENT (quotes already escaped). </summary>
public sealed class JunctionCall
{
    private readonly SqlDialect _dialect;
    private readonly string _schema;
    private readonly string _entity;
    private readonly string _anchorParameter;
    private readonly bool _hasCreateUser;

    /// <param name="entity"> The name the routines were generated under (the table's name, Table_List ...). </param>
    /// <param name="anchorColumnName"> The anchor column's name; the List parameter is called Anchor + this. </param>
    public JunctionCall(TableModel model, string entity, string anchorColumnName)
    {
        _dialect = model.Dialect;
        _schema = model.SchemaName;
        _entity = entity;
        _anchorParameter = "@Anchor" + anchorColumnName;
        _hasCreateUser = model.Columns.Any(c => c.IsCreateUserColumn);
    }

    /// <summary> The List statement; its one parameter is <see cref="AnchorParameter"/>. </summary>
    public string ListSql => _dialect switch
    {
        SqlDialect.PostgreSql => $"SELECT * FROM \\\"{_schema}\\\".\\\"{_entity}_List\\\"({_anchorParameter})",
        SqlDialect.MySql => $"CALL `{_entity}_List`({_anchorParameter})",
        _ => $"EXEC [{_schema}].[{_entity}_List] {_anchorParameter}"
    };

    /// <summary> A C# expression creating the List statement's parameter (its type is the database's own parameter class, written with its namespace). </summary>
    public string AnchorParameter(string valueExpression) => _dialect switch
    {
        SqlDialect.PostgreSql => $"new Npgsql.NpgsqlParameter(\"{_anchorParameter}\", {valueExpression})",
        SqlDialect.MySql => $"new MySql.Data.MySqlClient.MySqlParameter(\"{_anchorParameter}\", {valueExpression})",
        _ => $"new Microsoft.Data.SqlClient.SqlParameter(\"{_anchorParameter}\", {valueExpression})"
    };

    /// <summary> The Link statement as the text of an interpolated string for ExecuteSqlInterpolatedAsync; the two arguments are C# expressions. A
    /// MySQL procedure has no default parameter, so a table with a create-user column gets an explicit NULL for it. </summary>
    public string LinkSql(string anchorExpression, string targetExpression) => Interpolated("Link", anchorExpression, targetExpression, _hasCreateUser);

    public string UnlinkSql(string anchorExpression, string targetExpression) => Interpolated("Unlink", anchorExpression, targetExpression, false);

    private string Interpolated(string routine, string anchor, string target, bool trailingNull) => _dialect switch
    {
        SqlDialect.PostgreSql => $"SELECT \\\"{_schema}\\\".\\\"{_entity}_{routine}\\\"({{{anchor}}}, {{{target}}})",
        SqlDialect.MySql => $"CALL `{_entity}_{routine}`({{{anchor}}}, {{{target}}}{(trailingNull ? ", NULL" : "")})",
        _ => $"EXEC [{_schema}].[{_entity}_{routine}] {{{anchor}}}, {{{target}}}"
    };
}
