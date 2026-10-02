using System.Globalization;

namespace CodeGenNew.Core;

/// <summary> Turns a value read from a MySQL column (TableModel.Rows) into a MySQL literal that loads back to the same value: backslashes and quotes escaped
/// (MySQL treats a backslash in a string literal as an escape), 1/0 for a boolean, ISO timestamps, invariant-culture numbers, 0x binary. </summary>
public static class MySqlLiteral
{
    public static string Format(ColumnModel column, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return "NULL";
            case string s:
                return Quote(s);
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
                return "'" + dt.ToString(column.SqlType == System.Data.SqlDbType.Date ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "'";
            case DateOnly date:
                return "'" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'";
            case TimeSpan ts:
                return "'" + ts.ToString(@"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture) + "'";
            case TimeOnly time:
                return "'" + time.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "'";
            case Guid g:
                return "'" + g.ToString("D") + "'";
            case byte[] bytes:
                return bytes.Length == 0 ? "''" : "0x" + Convert.ToHexString(bytes);
            default:
                return Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        }
    }

    private static string Quote(string text) => "'" + text.Replace("\\", "\\\\").Replace("'", "''") + "'";
}
