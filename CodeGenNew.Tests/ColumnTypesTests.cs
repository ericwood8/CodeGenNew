using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The type mapping and caption rules every template shares, checked once for every SQL type. </summary>
[TestClass]
public class ColumnTypesTests
{
    private static ColumnModel Of(SqlDbType type, bool nullable = false, int? characters = null, string name = "Value") =>
        Sample.Column(name, type, nullable: nullable, characters: characters);

    // SQL type, C#, F#, TypeScript, JSON Schema type, JSON Schema format
    [TestMethod]
    [DataRow(SqlDbType.Int, "int", "int", "number", "integer", "int32")]
    [DataRow(SqlDbType.BigInt, "long", "int64", "number", "integer", "int64")]
    [DataRow(SqlDbType.SmallInt, "short", "int16", "number", "integer", "int32")]
    [DataRow(SqlDbType.TinyInt, "byte", "byte", "number", "integer", "int32")]
    [DataRow(SqlDbType.Bit, "bool", "bool", "boolean", "boolean", null)]
    [DataRow(SqlDbType.Decimal, "decimal", "decimal", "number", "number", null)]
    [DataRow(SqlDbType.Money, "decimal", "decimal", "number", "number", null)]
    [DataRow(SqlDbType.SmallMoney, "decimal", "decimal", "number", "number", null)]
    [DataRow(SqlDbType.Float, "double", "double", "number", "number", "double")]
    [DataRow(SqlDbType.Real, "float", "float32", "number", "number", "float")]
    [DataRow(SqlDbType.Date, "DateTime", "DateTime", "string", "string", "date-time")]
    [DataRow(SqlDbType.DateTime, "DateTime", "DateTime", "string", "string", "date-time")]
    [DataRow(SqlDbType.DateTime2, "DateTime", "DateTime", "string", "string", "date-time")]
    [DataRow(SqlDbType.SmallDateTime, "DateTime", "DateTime", "string", "string", "date-time")]
    [DataRow(SqlDbType.DateTimeOffset, "DateTimeOffset", "DateTimeOffset", "string", "string", "date-time")]
    [DataRow(SqlDbType.Time, "TimeSpan", "TimeSpan", "string", "string", null)]
    [DataRow(SqlDbType.UniqueIdentifier, "Guid", "Guid", "string", "string", "uuid")]
    [DataRow(SqlDbType.Char, "string", "string", "string", "string", null)]
    [DataRow(SqlDbType.VarChar, "string", "string", "string", "string", null)]
    [DataRow(SqlDbType.NChar, "string", "string", "string", "string", null)]
    [DataRow(SqlDbType.NVarChar, "string", "string", "string", "string", null)]
    [DataRow(SqlDbType.Text, "string", "string", "string", "string", null)]
    [DataRow(SqlDbType.NText, "string", "string", "string", "string", null)]
    [DataRow(SqlDbType.Xml, "string", "string", "string", "string", null)]
    [DataRow(SqlDbType.Binary, "byte[]", "byte[]", "string", "string", "byte")]
    [DataRow(SqlDbType.VarBinary, "byte[]", "byte[]", "string", "string", "byte")]
    [DataRow(SqlDbType.Image, "byte[]", "byte[]", "string", "string", "byte")]
    [DataRow(SqlDbType.Timestamp, "byte[]", "byte[]", "string", "string", "byte")]
    [DataRow(SqlDbType.Variant, "object", "obj", "string", null, null)]
    public void Every_sql_type_maps_to_the_same_type_in_each_language(SqlDbType type, string csharp, string fsharp, string typeScript, string? jsonType, string? jsonFormat)
    {
        var column = Of(type);

        Assert.AreEqual(csharp, column.CSharpBase());
        Assert.AreEqual(fsharp, column.FSharpBase());
        Assert.AreEqual(typeScript, column.TypeScript());
        Assert.AreEqual((jsonType, jsonFormat), column.JsonSchema());
    }

    [TestMethod]
    public void Nullability_adds_a_question_mark_in_C_Sharp_and_option_in_F_Sharp()
    {
        Assert.AreEqual("int", Of(SqlDbType.Int).CSharp());
        Assert.AreEqual("int?", Of(SqlDbType.Int, nullable: true).CSharp());
        Assert.AreEqual("string?", Of(SqlDbType.NVarChar, nullable: true).CSharp());
        Assert.AreEqual("object?", Of(SqlDbType.Variant, nullable: true).CSharp());
        Assert.AreEqual("int option", Of(SqlDbType.Int, nullable: true).FSharp());
        Assert.AreEqual("string", Of(SqlDbType.NVarChar).FSharp());
    }

    [TestMethod]
    [DataRow(SqlDbType.Int, "Parse.int32")]
    [DataRow(SqlDbType.BigInt, "Parse.int64")]
    [DataRow(SqlDbType.SmallInt, "Parse.int16")]
    [DataRow(SqlDbType.TinyInt, "Parse.byte")]
    [DataRow(SqlDbType.Bit, "Parse.boolean")]
    [DataRow(SqlDbType.Money, "Parse.decimal")]
    [DataRow(SqlDbType.Float, "Parse.double")]
    [DataRow(SqlDbType.Real, "Parse.single")]
    [DataRow(SqlDbType.DateTime, "Parse.dateTime")]
    [DataRow(SqlDbType.DateTimeOffset, "Parse.dateTimeOffset")]
    [DataRow(SqlDbType.Time, "Parse.timeSpan")]
    [DataRow(SqlDbType.UniqueIdentifier, "Parse.guid")]
    [DataRow(SqlDbType.VarChar, "Parse.text")]
    [DataRow(SqlDbType.VarBinary, "Parse.base64")]
    [DataRow(SqlDbType.Variant, "Parse.untyped")]
    public void The_F_Sharp_parser_follows_the_type(SqlDbType type, string parser)
    {
        Assert.AreEqual(parser, Of(type).FSharpParser());
    }

    [TestMethod]
    public void A_reserved_word_is_written_with_an_at_sign()
    {
        Assert.AreEqual("@class", Of(SqlDbType.VarChar, name: "class").CSharpName());
        Assert.AreEqual("Name", Of(SqlDbType.VarChar, name: "Name").CSharpName());
    }

    [TestMethod]
    public void A_unicode_length_is_halved_and_an_unknown_one_is_zero()
    {
        Assert.AreEqual(50, Of(SqlDbType.NVarChar, characters: 50).CharacterLength());   // the sample stores 100 bytes
        Assert.AreEqual(50, Of(SqlDbType.VarChar, characters: 50).CharacterLength());
        Assert.AreEqual(0, Of(SqlDbType.Int).CharacterLength());
    }

    [TestMethod]
    [DataRow("CreditLimit", false, "Credit Limit")]
    [DataRow("SY_Role", false, "SY Role")]
    [DataRow("RequireCustomerPO", false, "Require Customer PO")]
    [DataRow("IsTaxable", true, "Taxable")]
    [DataRow("IsTaxable", false, "Is Taxable")]
    [DataRow("Issue", true, "Issue")]
    [DataRow("billing__city", false, "billing city")]
    [DataRow("E_DonateLeaveId", false, "E Donate Leave Id")]
    public void A_name_becomes_words(string name, bool isFlag, string expected)
    {
        Assert.AreEqual(expected, Labels.Words(name, isFlag));
    }
}
