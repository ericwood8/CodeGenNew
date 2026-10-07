using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> Small text helpers templates import with <c>using static</c>, so a template calls them without declaring its own copy. </summary>
public static class TemplateHelpers
{
    public static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    public static string UpperFirst(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary> A table's name without the E_ or SY_ prefix some databases put on it. </summary>
    public static string BaseName(string table) => Regex.Replace(Regex.Replace(table, "^E_", ""), "^SY_", "");

    public static bool IsText(ColumnModel c) => c.SqlType is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar;

    public static bool IsNumber(ColumnModel c) => c.IsIntegerColumn || c.IsNumericColumn || c.IsMoneyColumn;

    public static bool IsDate(ColumnModel c) => c.SqlType is SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime;

    public static bool IsGuid(ColumnModel c) => c.IsGuidColumn;

    public static bool IsBit(ColumnModel c) => c.IsBooleanColumn;

    public static bool IsEditableType(ColumnModel c) => IsDate(c) || IsNumber(c) || IsGuid(c) || IsBit(c) || IsText(c);

    /// <summary> A decimal, float or real column that is not money. </summary>
    public static bool IsPlainDecimal(ColumnModel c) => !c.IsCurrencyColumn && c.IsNumericColumn;

    public static int Chars(ColumnModel c) => c.CharacterLength();

    public static bool IsMultiline(ColumnModel c) => IsText(c) && Chars(c) >= 100;

    /// <summary> A number in the shortest text that reads back to the same value ("R"), in the invariant culture. </summary>
    public static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary> A number with up to eight decimals and no exponent, in the invariant culture. </summary>
    public static string Inv(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
