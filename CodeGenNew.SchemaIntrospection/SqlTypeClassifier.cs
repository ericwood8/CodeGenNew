using System.Data;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Retargets Avatar.CodeGen.SqlServer.DataLayer.Column's type-classification pattern from its old
/// custom SqlDataType enum onto System.Data.SqlDbType (per Docs/specs.md section 13, round-5 Q4). </summary>
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
        "xml" => SqlDbType.Xml,
        "sql_variant" => SqlDbType.Variant,
        _ => SqlDbType.Variant
    };

    public static bool IsIntegerColumn(SqlDbType t) =>
        t is SqlDbType.BigInt or SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt;

    public static bool IsNumericColumn(SqlDbType t) =>
        t is SqlDbType.Decimal or SqlDbType.Float or SqlDbType.Real;

    public static bool IsMoneyColumn(SqlDbType t) =>
        t is SqlDbType.Money or SqlDbType.SmallMoney;

    public static bool IsStringColumn(SqlDbType t) =>
        t is SqlDbType.Char or SqlDbType.NChar or SqlDbType.VarChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText;

    public static bool IsDateColumn(SqlDbType t) =>
        t is SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset;

    public static bool IsBooleanColumn(SqlDbType t) => t == SqlDbType.Bit;

    public static string BuildDeclaration(string sqlTypeName, SqlDbType type, int maxLength, int precision, int scale)
    {
        if (IsStringColumn(type) && type is not (SqlDbType.Text or SqlDbType.NText))
        {
            bool isUnicode = type is SqlDbType.NChar or SqlDbType.NVarChar;
            int declaredLength = isUnicode ? maxLength / 2 : maxLength;
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
