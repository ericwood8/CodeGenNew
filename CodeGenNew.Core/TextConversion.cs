namespace CodeGenNew.Core;

/// <summary> How the EF Core context turns a column's text into what the database stores, for the column types that are text to the generator but not text to the database: a PostgreSQL array, interval,
/// range or shape, a MySQL spatial column and a MySQL BIT(64) column. </summary>
public sealed record TextConversion(string Expression, string? ColumnType)
{
    /// <summary> The conversion of a column, or null for a column the database stores as text. </summary>
    public static TextConversion? Of(ColumnModel column)
    {
        if (column.PgText is { } pg)
            return new TextConversion($"{pg.Converter(column.IsNullable)}, PgText.Comparer", pg.Name);
        if (MySqlSpatialType.FromName(column.DbSpatialType) is { } spatial)
            return new TextConversion($"MySqlSpatial.Converter({column.DbSrid}), MySqlSpatial.Comparer", null);   // the provider maps the bytes as they are; naming the spatial type confuses its type mapping
        if (column.DbBitWidth == 64)
            return new TextConversion("new ValueConverter<decimal, ulong>(v => (ulong)v, v => v)", null);   // the driver reads BIT(64) as an unsigned 64-bit integer, which the entity holds as a decimal
        return null;
    }

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
