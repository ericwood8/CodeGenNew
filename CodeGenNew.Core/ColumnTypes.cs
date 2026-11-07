using System.Data;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> What a column is in each language the templates write, in one place: the SQL type to a C#, F# or TypeScript type or a JSON Schema type, a column's name as an identifier,
/// a text column's length in characters. A rule that must be the same in every template (and every platform) lives here, tested once, instead of being copied into each one. </summary>
public static class ColumnTypes
{
    /// <summary> The column's name as a C# identifier: <c>class</c> is written <c>@class</c>. </summary>
    public static string CSharpName(this ColumnModel column) => column.IsCSharpReservedWordName ? "@" + column.Name : column.Name;

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

}

/// <summary> Captions made from names. </summary>
public static partial class Labels
{
    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBreak();

    [GeneratedRegex("^Is[A-Z]")]
    private static partial Regex IsPrefix();

    [GeneratedRegex(" +")]
    private static partial Regex Spaces();

    [GeneratedRegex("^SY_")]
    private static partial Regex SystemPrefix();

    [GeneratedRegex("Id$")]
    private static partial Regex IdSuffix();

    /// <summary> A name as words: <c>CreditLimit</c> is "Credit Limit", <c>SY_Role</c> is "SY Role", <c>RequireCustomerPO</c> is "Require Customer PO". With <paramref name="isFlag"/> a leading
    /// <c>Is</c> is dropped first (<c>IsTaxable</c> is "Taxable"). </summary>
    public static string Words(string name, bool isFlag = false)
    {
        string s = name;
        if (isFlag && IsPrefix().IsMatch(s))
            s = s[2..];
        s = WordBreak().Replace(s.Replace('_', ' '), " ");
        return Spaces().Replace(s, " ").Trim();
    }

    /// <summary> The caption of a column: <c>SY_</c> and a trailing <c>Id</c> dropped, then as words (<c>CustomerId</c> is "Customer", <c>BillingAddress</c> is "Billing Address"). </summary>
    public static string Column(string columnName) => Words(IdSuffix().Replace(SystemPrefix().Replace(columnName, ""), ""));

    /// <summary> "Customer Item Number" is "Customer Item #", as on the grids; a caption that is only "Number" keeps its word. </summary>
    internal static string NumberSign(string caption)
    {
        var words = caption.Split(' ');
        if (words.Length < 2)
            return caption;
        for (int i = 0; i < words.Length; i++)
            if (words[i].EqualsIgnoreCase("Number") || words[i].EqualsIgnoreCase("Num"))
                words[i] = "#";
        return string.Join(' ', words);
    }

    /// <summary> The hint inside an empty text box: "Enter credit limit". </summary>
    public static string Placeholder(ColumnModel column) => "Enter " + Column(column.Name).ToLowerInvariant();
}
