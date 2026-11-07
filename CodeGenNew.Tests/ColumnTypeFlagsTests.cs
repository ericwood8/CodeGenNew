using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The type flags of a column come from its SqlType alone, so no provider sets them. </summary>
[TestClass]
public class ColumnTypeFlagsTests
{
    private static ColumnModel Of(SqlDbType type) => new()
    {
        Name = "C", QuotedName = "[C]", SqlType = type, SqlTypeDeclaration = type.ToString()
    };

    [TestMethod]
    public void Every_sql_type_lands_in_the_groups_the_providers_used_to_flag()
    {
        var integer = new[] { SqlDbType.BigInt, SqlDbType.Int, SqlDbType.SmallInt, SqlDbType.TinyInt };
        var numeric = new[] { SqlDbType.Decimal, SqlDbType.Float, SqlDbType.Real };
        var money = new[] { SqlDbType.Money, SqlDbType.SmallMoney };
        var text = new[] { SqlDbType.Char, SqlDbType.NChar, SqlDbType.VarChar, SqlDbType.NVarChar, SqlDbType.Text, SqlDbType.NText };
        var date = new[] { SqlDbType.Date, SqlDbType.DateTime, SqlDbType.DateTime2, SqlDbType.SmallDateTime, SqlDbType.DateTimeOffset };

        foreach (SqlDbType type in Enum.GetValues<SqlDbType>())
        {
            var column = Of(type);
            Assert.AreEqual(integer.Contains(type), column.IsIntegerColumn, $"{type} integer");
            Assert.AreEqual(numeric.Contains(type), column.IsNumericColumn, $"{type} numeric");
            Assert.AreEqual(money.Contains(type), column.IsMoneyColumn, $"{type} money");
            Assert.AreEqual(text.Contains(type), column.IsStringColumn, $"{type} string");
            Assert.AreEqual(date.Contains(type), column.IsDateColumn, $"{type} date");
            Assert.AreEqual(type == SqlDbType.Bit, column.IsBooleanColumn, $"{type} boolean");
        }
    }

    [TestMethod]
    public void A_column_works_out_its_numeric_kind_limits_and_percentage_from_its_own_name_and_type()
    {
        var month = Sample.Column("MonthNumber", SqlDbType.Int);
        Assert.AreEqual(NumericKind.Month, month.NumericKind);
        Assert.IsTrue(month.HasNumericLimits);
        Assert.AreEqual(new NumericRange(1, 12), month.RangeWith(2000, 2100));
        Assert.AreEqual(new NumericRange(2000, 2100), Sample.Column("Year", SqlDbType.Int).RangeWith(2000, 2100));

        var plain = Sample.Column("Description", SqlDbType.Int);
        Assert.AreEqual(NumericKind.None, plain.NumericKind);
        Assert.IsFalse(plain.HasNumericLimits);

        var checkedColumn = Sample.Column("Rating", SqlDbType.Int, check: new CheckRange(1, false, 5, false));
        Assert.IsTrue(checkedColumn.HasNumericLimits, "a CHECK constraint is a limit");

        var share = Sample.Column("DiscountPercent", SqlDbType.Decimal, precision: 5, scale: 2);
        Assert.IsTrue(share.IsPercentage);
        Assert.AreEqual(NumericKind.None, share.NumericKind, "the kind is for whole-number columns");
        Assert.IsNull(share.RangeWith(2000, 2100));
    }

    [TestMethod]
    public void The_exact_type_properties_name_one_type_each_and_the_groups_add_up()
    {
        foreach (SqlDbType type in Enum.GetValues<SqlDbType>())
        {
            var column = Of(type);
            Assert.AreEqual(type == SqlDbType.Int, column.IsInt32Column, $"{type} int");
            Assert.AreEqual(type == SqlDbType.BigInt, column.IsBigIntColumn, $"{type} bigint");
            Assert.AreEqual(type == SqlDbType.Date, column.IsDateOnlyColumn, $"{type} date");
            Assert.AreEqual(type is SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime, column.IsDateTimeColumn, $"{type} datetime");
            Assert.AreEqual(column.IsDateOnlyColumn || column.IsDateTimeColumn, column.IsLocalDateColumn, $"{type} local date");
            Assert.AreEqual(type is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar, column.IsPlainTextColumn, $"{type} plain text");
            Assert.AreEqual(type is SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image, column.IsBinaryColumn, $"{type} binary");
            Assert.AreEqual(column.IsLargeTextColumn || type == SqlDbType.Image, column.IsLargeObjectColumn, $"{type} large object");
        }
        Assert.AreEqual("int", Of(SqlDbType.Int).CSharpBase());
        Assert.AreEqual("number", Of(SqlDbType.Decimal).TypeScript());
        Assert.AreEqual("google.protobuf.Timestamp", Of(SqlDbType.DateTime2).Proto());
        Assert.AreEqual(("string", "uuid"), Of(SqlDbType.UniqueIdentifier).JsonSchema());
    }
}
