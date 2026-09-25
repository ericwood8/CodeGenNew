using Microsoft.Data.SqlClient;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Reads a SqlDataReader column by name instead of the usual reader.GetXxx(reader.GetOrdinal("X"))
/// pair -- these overload the same method names the reader already exposes, so they show up as a natural
/// IntelliSense option wherever reader.GetString(...) etc. would. </summary>
public static class SqlDataReaderExtensions
{
    public static string GetString(this SqlDataReader reader, string columnName) => reader.GetString(reader.GetOrdinal(columnName));
    public static int GetInt32(this SqlDataReader reader, string columnName) => reader.GetInt32(reader.GetOrdinal(columnName));
    public static short GetInt16(this SqlDataReader reader, string columnName) => reader.GetInt16(reader.GetOrdinal(columnName));
    public static byte GetByte(this SqlDataReader reader, string columnName) => reader.GetByte(reader.GetOrdinal(columnName));
    public static bool GetBoolean(this SqlDataReader reader, string columnName) => reader.GetBoolean(reader.GetOrdinal(columnName));
    public static bool IsDBNull(this SqlDataReader reader, string columnName) => reader.IsDBNull(reader.GetOrdinal(columnName));

    public static string? GetNullableString(this SqlDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    // seed_value/increment_value-style sql_variant columns whose underlying numeric type varies -- read via
    // GetValue + Convert rather than assuming a fixed CLR type like GetDecimal.
    public static int? GetNullableInt32(this SqlDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }
}
