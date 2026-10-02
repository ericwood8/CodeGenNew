namespace CodeGenNew.Core;

/// <summary> How a column's listed values (<see cref="ColumnModel.Choices"/>) are written into generated text. </summary>
public static class ChoiceFormat
{
    /// <summary> Text safe as HTML / JSX / Angular template content and as a double-quoted attribute value. </summary>
    public static string Html(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")
        .Replace("{", "&#123;").Replace("}", "&#125;");

    /// <summary> The content of a C# string literal (without the quotes). </summary>
    public static string CSharp(string value) => value
        .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\r").Replace("\n", "\n").Replace("\t", "\t");
}
