namespace CodeGenNew.Core;

/// <summary> How the EF Core context turns a column's text into what the database stores, for the column types that are text to the generator but not text to the database (<see cref="ColumnModel.Conversion"/> decides which): a PostgreSQL array, interval,
/// range or shape, a MySQL spatial column and a MySQL BIT(64) column. </summary>
public sealed record TextConversion(string Expression, string? ColumnType)
{
    /// <summary> The usings the helper classes need, and their source, for the columns of a database (empty when no column needs one). </summary>
    public static (List<string> Usings, string Source) Helpers(IEnumerable<ColumnModel> columns)
    {
        var usings = new List<string>();
        var source = new List<string>();
        var list = columns.ToList();
        if (list.Any(c => c.PgText is not null))
        {
            usings.AddRange(["System.Globalization", "System.Text", "System.Text.RegularExpressions", "NpgsqlTypes"]);
            source.Add(PgTextSource.Code);
        }
        if (list.Any(c => c.DbSpatialType is not null))
        {
            usings.AddRange(["System.Globalization"]);
            source.Add(MySqlSpatialSource.Code);
        }
        return (usings.Distinct().ToList(), string.Join("\n\n", source));
    }
}
