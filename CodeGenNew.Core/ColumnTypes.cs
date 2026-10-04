using System.Data;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> What a column is in each language the templates write, in one place: the SQL type to a C#, F# or TypeScript type or a JSON Schema type, a column's name as an identifier,
/// a text column's length in characters. A rule that must be the same in every template (and every platform) lives here, tested once, instead of being copied into each one. </summary>
public static class ColumnTypes
{
    /// <summary> The column's name as a C# identifier: <c>class</c> is written <c>@class</c>. </summary>
    public static string CSharpName(this ColumnModel column) => column.IsCSharpReservedWordName ? "@" + column.Name : column.Name;

    /// <summary> The C# type without nullability: <c>int</c>, <c>string</c>, <c>byte[]</c>; <c>object</c> for a type with no mapping. </summary>
    public static string CSharpBase(this ColumnModel column) => column.SqlType switch
    {
        SqlDbType.Int => "int",
        SqlDbType.BigInt => "long",
        SqlDbType.SmallInt => "short",
        SqlDbType.TinyInt => "byte",
        SqlDbType.Bit => "bool",
        SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => "decimal",
        SqlDbType.Float => "double",
        SqlDbType.Real => "float",
        SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime => "DateTime",
        SqlDbType.DateTimeOffset => "DateTimeOffset",
        SqlDbType.Time => "TimeSpan",
        SqlDbType.UniqueIdentifier => "Guid",
        SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml => "string",
        SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Timestamp => "byte[]",
        _ => "object"
    };

    /// <summary> The C# type as an entity property has it: the base type, with <c>?</c> when the column can be NULL (<c>int?</c>, <c>string?</c>, <c>object?</c>). </summary>
    public static string CSharp(this ColumnModel column) => column.IsNullable ? column.CSharpBase() + "?" : column.CSharpBase();

    /// <summary> The F# type without <c>option</c>: <c>int</c>, <c>decimal</c>, <c>DateTime</c>, <c>float32</c>, <c>byte[]</c>; <c>obj</c> for a type with no mapping. </summary>
    public static string FSharpBase(this ColumnModel column) =>
        column.CSharpBase() switch { "short" => "int16", "long" => "int64", "float" => "float32", "object" => "obj", var other => other };

    /// <summary> The F# type as a record field has it: <c>T option</c> when the column can be NULL. </summary>
    public static string FSharp(this ColumnModel column) => column.IsNullable ? column.FSharpBase() + " option" : column.FSharpBase();

    /// <summary> The text parser of the F# <c>Rop.Parse</c> module (written by FS_Rop) that reads this column's type: <c>Parse.int32</c>, <c>Parse.dateTime</c> ... </summary>
    public static string FSharpParser(this ColumnModel column) => column.FSharpBase() switch
    {
        "int" => "Parse.int32",
        "int64" => "Parse.int64",
        "int16" => "Parse.int16",
        "byte" => "Parse.byte",
        "bool" => "Parse.boolean",
        "decimal" => "Parse.decimal",
        "double" => "Parse.double",
        "float32" => "Parse.single",
        "DateTime" => "Parse.dateTime",
        "DateTimeOffset" => "Parse.dateTimeOffset",
        "TimeSpan" => "Parse.timeSpan",
        "Guid" => "Parse.guid",
        "string" => "Parse.text",
        "byte[]" => "Parse.base64",
        _ => "Parse.untyped"
    };

    /// <summary> The TypeScript type of the value the API's JSON holds: <c>number</c> for every number (a decimal too), <c>boolean</c>, else <c>string</c> (a date is its ISO text). </summary>
    public static string TypeScript(this ColumnModel column) => column.SqlType switch
    {
        SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt or SqlDbType.TinyInt or SqlDbType.Decimal or SqlDbType.Money
            or SqlDbType.SmallMoney or SqlDbType.Float or SqlDbType.Real => "number",
        SqlDbType.Bit => "boolean",
        _ => "string"
    };

    /// <summary> The protobuf type: <c>int32</c>, <c>int64</c>, <c>bool</c>, <c>float</c>, <c>double</c>, <c>bytes</c>, <c>google.protobuf.Timestamp</c> for a date, and <c>string</c> for text, a decimal (protobuf has no decimal type), a time or a GUID. </summary>
    public static string Proto(this ColumnModel column) => column.SqlType switch
    {
        SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt => "int32",
        SqlDbType.BigInt => "int64",
        SqlDbType.Bit => "bool",
        SqlDbType.Float => "double",
        SqlDbType.Real => "float",
        SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset => "google.protobuf.Timestamp",
        SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Timestamp => "bytes",
        SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney or SqlDbType.Time or SqlDbType.UniqueIdentifier
            or SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml => "string",
        _ => "bytes"
    };

    /// <summary> The JSON Schema / OpenAPI <c>type</c> and <c>format</c> of the column's JSON value; the type is null for a database type with no JSON form. </summary>
    public static (string? Type, string? Format) JsonSchema(this ColumnModel column) => column.SqlType switch
    {
        SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt => ("integer", "int32"),
        SqlDbType.BigInt => ("integer", "int64"),
        SqlDbType.Bit => ("boolean", null),
        SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => ("number", null),
        SqlDbType.Float => ("number", "double"),
        SqlDbType.Real => ("number", "float"),
        SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset => ("string", "date-time"),
        SqlDbType.Time => ("string", null),
        SqlDbType.UniqueIdentifier => ("string", "uuid"),
        SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Timestamp => ("string", "byte"),
        SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml => ("string", null),
        _ => (null, null)
    };

    /// <summary> A text column's length in characters. The schema reader states the length of a Unicode column (<c>nchar</c>, <c>nvarchar</c>) in bytes, so it is halved; 0 when the length is not known,
    /// and a negative number for <c>varchar(max)</c> (callers test <c>&gt; 0</c>). </summary>
    public static int CharacterLength(this ColumnModel column) =>
        column.MaxLength is null ? 0 : column.SqlType is SqlDbType.NChar or SqlDbType.NVarChar ? column.MaxLength.Value / 2 : column.MaxLength.Value;
}

/// <summary> Captions made from names. </summary>
public static class Labels
{
    private static readonly Regex WordBreak = new("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    /// <summary> A name as words: <c>CreditLimit</c> is "Credit Limit", <c>SY_Role</c> is "SY Role", <c>RequireCustomerPO</c> is "Require Customer PO". With <paramref name="isFlag"/> a leading
    /// <c>Is</c> is dropped first (<c>IsTaxable</c> is "Taxable"). </summary>
    public static string Words(string name, bool isFlag = false)
    {
        string s = name;
        if (isFlag && Regex.IsMatch(s, "^Is[A-Z]"))
            s = s[2..];
        s = WordBreak.Replace(s.Replace('_', ' '), " ");
        return Regex.Replace(s, " +", " ").Trim();
    }
}
