using System.Globalization;

namespace CodeGenNew.Core;

/// <summary> Turns a value read from a PostgreSQL column (TableModel.Rows) into a PostgreSQL literal that loads back to the same value:
/// doubled quotes, true/false, ISO timestamps, invariant-culture numbers, '\x..'::bytea. The T-SQL counterpart is <see cref="SqlLiteral"/>. </summary>
public static class PostgresLiteral
{
    public static string Format(ColumnModel column, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return "NULL";
            case string s:
                return "'" + s.Replace("'", "''") + "'";
            case bool b:
                return column.IsBooleanColumn ? (b ? "true" : "false") : (b ? "1" : "0");
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
            case DateTimeOffset dto:
                return "'" + dto.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture) + "'";
            case TimeSpan ts:
                return "'" + ts.ToString(@"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture) + "'";
            case TimeOnly time:
                return "'" + time.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "'";
            case Guid g:
                return "'" + g.ToString("D") + "'";
            case byte[] bytes:
                return "'\\x" + Convert.ToHexString(bytes).ToLowerInvariant() + "'::bytea";
            default:
                return "'" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("'", "''") + "'";
        }
    }
}
