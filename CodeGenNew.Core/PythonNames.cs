using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> Names as Python writes them: snake_case attributes, modules and packages, PascalCase classes, and a word Python, SQLAlchemy or Pydantic already uses made legal by a trailing underscore. </summary>
public static partial class PythonNames
{
    // Keywords, plus the names a declarative class or a Pydantic model already has (a field called "metadata" or "copy" would shadow them).
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "false", "none", "true", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global", "if",
        "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield", "self", "cls",
        "metadata", "registry", "copy", "dict", "json", "schema", "validate", "construct", "fields", "columns", "model_config", "model_fields"
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal) { "False", "None", "True" };

    /// <summary> <c>BillingAddress1</c> gives <c>billing_address1</c>, <c>RequireCustomerPO</c> gives <c>require_customer_po</c>, <c>customer-item</c> gives <c>customer_item</c>. </summary>
    public static string Snake(string name) => RustNames.Snake(name);

    /// <summary> A snake_case name that is legal as an attribute: a keyword, or a word the base classes use, gets an underscore after it. </summary>
    public static string Ident(string snake) => Reserved.Contains(snake) || Keywords.Contains(snake) ? snake + "_" : snake;

    /// <summary> The name as a class: <c>SalesInvoice</c> stays, <c>E_DonateLeave</c> gives <c>EDonateLeave</c>, <c>customer_item</c> gives <c>CustomerItem</c>. </summary>
    public static string Pascal(string name)
    {
        var parts = NonWord().Split(name).Where(p => p.Length > 0).ToList();
        string result = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return result.Length == 0 ? "T" : char.IsDigit(result[0]) ? "T" + result : result;
    }

    /// <summary> A Python string literal: double quotes, backslash and line breaks escaped. </summary>
    public static string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonWord();
}
