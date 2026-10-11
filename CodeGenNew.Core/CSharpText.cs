namespace CodeGenNew.Core;

/// <summary> Text written into generated C# (and TypeScript) source: string literals and property initializers. </summary>
public static class CSharpText
{
    /// <summary> The content of a double-quoted string literal, without the quotes. </summary>
    public static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary> A double-quoted string literal. </summary>
    public static string Quote(string text) => "\"" + Escape(text) + "\"";

    /// <summary> The initializer that keeps a NOT NULL string or byte array from being null (" = "";" / " = [];"), else empty. </summary>
    public static string NotNullInitializer(ColumnModel column) =>
        column.IsNullable ? "" : column.CSharpBase() switch { "string" => " = \"\";", "byte[]" => " = [];", _ => "" };
}
