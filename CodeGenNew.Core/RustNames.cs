using System.Text;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> Names as Rust writes them: snake_case fields, files and crates, PascalCase types, and a keyword used as a name made legal. </summary>
public static partial class RustNames
{
    // Reserved words (strict and reserved for the future, edition 2024). A raw identifier (r#type) is the legal way to use one, except the few that can never be raw.
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "as", "async", "await", "break", "const", "continue", "dyn", "else", "enum", "extern", "false", "fn", "for", "gen", "if", "impl", "in", "let", "loop", "match", "mod", "move",
        "mut", "pub", "ref", "return", "static", "struct", "trait", "true", "type", "unsafe", "use", "where", "while", "abstract", "become", "box", "do", "final", "macro", "override",
        "priv", "try", "typeof", "unsized", "virtual", "yield"
    };

    private static readonly HashSet<string> NeverRaw = new(StringComparer.Ordinal) { "self", "Self", "super", "crate", "_" };

    /// <summary> <c>BillingAddress1</c> gives <c>billing_address1</c>, <c>RequireCustomerPO</c> gives <c>require_customer_po</c>, <c>customer-item</c> gives <c>customer_item</c>. </summary>
    public static string Snake(string name)
    {
        var o = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (o.Length > 0 && o[^1] != '_')
                    o.Append('_');
                continue;
            }

            bool upper = char.IsUpper(c);
            bool afterLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
            bool endsAcronym = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);
            if (upper && (afterLowerOrDigit || endsAcronym) && o.Length > 0 && o[^1] != '_')
                o.Append('_');
            o.Append(char.ToLowerInvariant(c));
        }

        string result = o.ToString().Trim('_');
        return result.Length == 0 ? "_" : char.IsDigit(result[0]) ? "_" + result : result;
    }

    /// <summary> A snake_case name that is legal as a Rust identifier: a keyword becomes a raw identifier (<c>r#type</c>), and a word that cannot be raw (<c>self</c>) gets an underscore. </summary>
    public static string Ident(string snake) => NeverRaw.Contains(snake) ? snake + "_" : Keywords.Contains(snake) ? "r#" + snake : snake;

    /// <summary> The name as a type: <c>SalesInvoice</c> stays, <c>E_DonateLeave</c> gives <c>EDonateLeave</c>, <c>customer_item</c> gives <c>CustomerItem</c>. </summary>
    public static string Pascal(string name)
    {
        var parts = NonWord().Split(name).Where(p => p.Length > 0).ToList();
        string result = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return result.Length == 0 ? "T" : char.IsDigit(result[0]) ? "T" + result : result;
    }

    /// <summary> The name of a crate (a package) from a project name: lower case with underscores. </summary>
    public static string Crate(string projectName) => Snake(projectName);

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonWord();
}
