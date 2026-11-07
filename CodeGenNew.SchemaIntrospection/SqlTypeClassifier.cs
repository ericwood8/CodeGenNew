using CodeGenNew.Core;
using System.Data;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Groups a SQL Server type name onto System.Data.SqlDbType and the coarser type-family flags
/// (integer/money/string/date/boolean) the rest of the generator classifies columns by
/// (Docs/ARCHITECTURE.md section 4). </summary>
public static class SqlTypeClassifier
{
    public static SqlDbType MapSqlTypeName(string sqlTypeName) => sqlTypeName.ToLowerInvariant() switch
    {
        "bigint" => SqlDbType.BigInt,
        "int" => SqlDbType.Int,
        "smallint" => SqlDbType.SmallInt,
        "tinyint" => SqlDbType.TinyInt,
        "bit" => SqlDbType.Bit,
        "decimal" or "numeric" => SqlDbType.Decimal,
        "money" => SqlDbType.Money,
        "smallmoney" => SqlDbType.SmallMoney,
        "float" => SqlDbType.Float,
        "real" => SqlDbType.Real,
        "date" => SqlDbType.Date,
        "datetime" => SqlDbType.DateTime,
        "datetime2" => SqlDbType.DateTime2,
        "smalldatetime" => SqlDbType.SmallDateTime,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        "time" => SqlDbType.Time,
        "char" => SqlDbType.Char,
        "nchar" => SqlDbType.NChar,
        "varchar" => SqlDbType.VarChar,
        "nvarchar" => SqlDbType.NVarChar,
        "text" => SqlDbType.Text,
        "ntext" => SqlDbType.NText,
        "binary" => SqlDbType.Binary,
        "varbinary" => SqlDbType.VarBinary,
        "image" => SqlDbType.Image,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "timestamp" or "rowversion" => SqlDbType.Timestamp,
        "xml" => SqlDbType.Xml,
        "sql_variant" => SqlDbType.Variant,
        _ => SqlDbType.Variant
    };

    public static bool IsIntegerColumn(SqlDbType t) => SqlTypeKinds.IsInteger(t);

    public static bool IsNumericColumn(SqlDbType t) => SqlTypeKinds.IsNumeric(t);

    public static bool IsMoneyColumn(SqlDbType t) => SqlTypeKinds.IsMoney(t);

    public static bool IsStringColumn(SqlDbType t) => SqlTypeKinds.IsString(t);

    public static bool IsDateColumn(SqlDbType t) => SqlTypeKinds.IsDate(t);

    public static bool IsBooleanColumn(SqlDbType t) => SqlTypeKinds.IsBoolean(t);

    public static string BuildDeclaration(string sqlTypeName, SqlDbType type, int maxLength, int precision, int scale)
    {
        if (IsStringColumn(type) && type is not (SqlDbType.Text or SqlDbType.NText))
        {
            bool isUnicode = type is SqlDbType.NChar or SqlDbType.NVarChar;
            int declaredLength = maxLength < 0 ? -1 : isUnicode ? maxLength / 2 : maxLength;   // -1 is MAX; halving it would give 0
            string lengthText = declaredLength < 0 ? "MAX" : declaredLength.ToString();
            return $"{sqlTypeName}({lengthText})";
        }

        if (type == SqlDbType.Decimal)
            return $"{sqlTypeName}({precision},{scale})";

        if (type is SqlDbType.Binary or SqlDbType.VarBinary)
        {
            string lengthText = maxLength < 0 ? "MAX" : maxLength.ToString();
            return $"{sqlTypeName}({lengthText})";
        }

        return sqlTypeName;
    }
}
