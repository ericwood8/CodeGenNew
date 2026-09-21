using System.Data;
using System.Globalization;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> Turns a value read from a column (see TableModel.Rows) into a T-SQL literal that reloads to the same
/// value: language/DATEFORMAT-independent dates, invariant-culture numbers, doubled quotes, N-prefixed Unicode, 0x binary. </summary>
public static class SqlLiteral
{
    public static string Format(ColumnModel column, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return "NULL";
            case string s:
                return Quote(column, s);
            case bool b:
                return b ? "1" : "0";
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
            case decimal d:
                return d.ToString(CultureInfo.InvariantCulture);
            case double dbl:
                return dbl.ToString("R", CultureInfo.InvariantCulture);
            case float f:
                return f.ToString("R", CultureInfo.InvariantCulture);
            case DateTime dt:
                return "'" + dt.ToString(DateFormatFor(column.SqlType), CultureInfo.InvariantCulture) + "'";
            case DateTimeOffset dto:
                return "'" + dto.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture) + "'";
            case TimeSpan ts:
                return "'" + ts.ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture) + "'";
            case Guid g:
                return "'" + g.ToString("D").ToUpperInvariant() + "'";
            case byte[] bytes:
                return "0x" + Convert.ToHexString(bytes);
            default:
                return Quote(column, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        }
    }

    private static string Quote(ColumnModel column, string text)
    {
        // N-prefix for Unicode columns, and for any string holding a non-ASCII character even in a (n)varchar
        // column, so the literal can never be mangled by the script file's / session's code page.
        bool unicode = column.SqlType is SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.NText or SqlDbType.Xml
                       || text.Any(ch => ch > 127);
        var sb = new StringBuilder(text.Length + 3);
        if (unicode)
            sb.Append('N');
        sb.Append('\'').Append(text.Replace("'", "''")).Append('\'');
        return sb.ToString();
    }

    // The 'T' separator form is the ISO 8601 format SQL Server reads the same under every SET DATEFORMAT / language.
    private static string DateFormatFor(SqlDbType type) => type switch
    {
        SqlDbType.Date => "yyyy-MM-dd",
        SqlDbType.SmallDateTime => "yyyy-MM-ddTHH:mm:ss",
        SqlDbType.DateTime2 => "yyyy-MM-ddTHH:mm:ss.fffffff",
        _ => "yyyy-MM-ddTHH:mm:ss.fff" // datetime
    };
}
