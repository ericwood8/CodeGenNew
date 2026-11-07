using System.Data;

namespace CodeGenNew.Core;

/// <summary> The groups the generator sorts a <see cref="SqlDbType"/> into. A column answers from these (<see cref="ColumnModel.IsIntegerColumn"/> and the like); the schema readers use them
/// before the column exists. </summary>
public static class SqlTypeKinds
{
    public static bool IsInteger(SqlDbType t) => t is SqlDbType.BigInt or SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt;

    public static bool IsNumeric(SqlDbType t) => t is SqlDbType.Decimal or SqlDbType.Float or SqlDbType.Real;

    public static bool IsMoney(SqlDbType t) => t is SqlDbType.Money or SqlDbType.SmallMoney;

    public static bool IsString(SqlDbType t) => t is SqlDbType.Char or SqlDbType.NChar or SqlDbType.VarChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText;

    public static bool IsDate(SqlDbType t) => t is SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset;

    public static bool IsBoolean(SqlDbType t) => t == SqlDbType.Bit;
}
