using System.Data;
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

    public static bool IsNumber(ColumnModel c) => c.SqlType is SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt or SqlDbType.TinyInt
        or SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney or SqlDbType.Float or SqlDbType.Real;

    public static bool IsDate(ColumnModel c) => c.SqlType is SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime;

    public static bool IsGuid(ColumnModel c) => c.SqlType == SqlDbType.UniqueIdentifier;

    public static bool IsBit(ColumnModel c) => c.SqlType == SqlDbType.Bit;

    public static bool IsEditableType(ColumnModel c) => IsDate(c) || IsNumber(c) || IsGuid(c) || IsBit(c) || IsText(c);

    /// <summary> A decimal, float or real column that is not money. </summary>
    public static bool IsPlainDecimal(ColumnModel c) => !c.IsIntegerColumn && !c.IsCurrencyColumn && c.SqlType is SqlDbType.Decimal or SqlDbType.Float or SqlDbType.Real;

    public static int Chars(ColumnModel c) => c.CharacterLength();

    public static bool IsMultiline(ColumnModel c) => IsText(c) && Chars(c) >= 100;
}
