using System.Data;
using System.Globalization;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> Turns a value read from a column (see TableModel.Rows) into a C# expression that builds the same value: culture-independent numbers, escaped text, dates as <c>new DateTime(...)</c>.
/// Null for a value C# has no short literal for (binary, a column type the generated entity does not carry), so the caller leaves that property out. </summary>
public static class CSharpLiteral
{
    /// <summary> The expression for a value read from <paramref name="column"/>, in the C# type the generated entity gives the column (a SQLite integer is read as a long, a bit as a number), or null when
    /// it is null or has no literal. </summary>
    public static string? Format(ColumnModel column, object? value)
    {
        if (value is null or DBNull)
            return null;
        try
        {
            if (column.IsBooleanColumn)
                return Format(value is string flag ? flag is "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase) : Convert.ToBoolean(value, CultureInfo.InvariantCulture));
            return column.SqlType switch
            {
                SqlDbType.Int => Format(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
                SqlDbType.SmallInt => Format(Convert.ToInt16(value, CultureInfo.InvariantCulture)),
                SqlDbType.TinyInt => Format(Convert.ToByte(value, CultureInfo.InvariantCulture)),
                SqlDbType.BigInt => Format(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => Format(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
                SqlDbType.Float => Format(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
                SqlDbType.Real => Format(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
                SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime => Format(value as DateTime? ?? DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)),
                SqlDbType.UniqueIdentifier => Format(value as Guid? ?? Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!)),
                _ when column.IsStringColumn => Format(Convert.ToString(value, CultureInfo.InvariantCulture)),
                _ => Format(value)
            };
        }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    /// <summary> The expression for <paramref name="value"/> as it is, or null when it is null or has no literal. </summary>
    public static string? Format(object? value) => value switch
    {
        null or DBNull => null,
        string text => Text(text),
        bool flag => flag ? "true" : "false",
        byte or sbyte or short or ushort or int or uint => Convert.ToString(value, CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
        double number => double.IsFinite(number) ? TemplateHelpers.Num(number) + "d" : null,
        float number => float.IsFinite(number) ? TemplateHelpers.Num(number) + "f" : null,
        DateTime moment => Moment(moment),
        Guid id => $"Guid.Parse(\"{id:D}\")",
        _ => null
    };

    private static string Text(string text)
    {
        var sb = new StringBuilder(text.Length + 2).Append('"');
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ': sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static string Moment(DateTime moment)
    {
        string parts = $"{moment.Year}, {moment.Month}, {moment.Day}";
        if (moment.TimeOfDay == TimeSpan.Zero)
            return $"new DateTime({parts})";
        string time = $"{parts}, {moment.Hour}, {moment.Minute}, {moment.Second}";
        long ticksOfSecond = moment.Ticks % TimeSpan.TicksPerSecond;
        return ticksOfSecond == 0 ? $"new DateTime({time})" : $"new DateTime({time}).AddTicks({ticksOfSecond.ToString(CultureInfo.InvariantCulture)})";
    }
}
