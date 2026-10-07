using CodeGenNew.Core;
using System.Text;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> What only the text of a SQLite <c>CREATE TABLE</c> statement says (<c>sqlite_master.sql</c>): the CHECK constraints, whether the table is <c>WITHOUT ROWID</c> or <c>STRICT</c>. SQLite's
/// pragmas report columns, keys, foreign keys and indexes but not CHECK constraints, so the statement is cut apart here. The scanner follows SQLite's quoting (<c>'text'</c>, <c>"name"</c>,
/// <c>`name`</c>, <c>[name]</c>) and comments, so a comma or a parenthesis inside a literal or an identifier is never taken for structure. </summary>
public static class SqliteDdl
{
    /// <summary> One CHECK constraint: the column it was written on (null for a table constraint, which may name any column) and the expression inside the parentheses. </summary>
    public sealed record CheckConstraint(string? Column, string Expression);

    public sealed record TableDefinition(IReadOnlyList<CheckConstraint> Checks, bool WithoutRowId, bool Strict);

    public static TableDefinition Parse(string? createSql)
    {
        if (string.IsNullOrWhiteSpace(createSql))
            return new TableDefinition([], false, false);

        int open = IndexOfOutsideQuotes(createSql, '(', 0);
        if (open < 0)
            return new TableDefinition([], false, false);
        int close = MatchingParenthesis(createSql, open);
        if (close < 0)
            return new TableDefinition([], false, false);

        string body = createSql[(open + 1)..close];
        string options = createSql[(close + 1)..];
        bool withoutRowId = options.ContainsIgnoreCase("WITHOUT") && options.ContainsIgnoreCase("ROWID");
        bool strict = System.Text.RegularExpressions.Regex.IsMatch(options, @"\bSTRICT\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var checks = new List<CheckConstraint>();
        foreach (string segment in SplitTopLevel(body))
        {
            string trimmed = segment.Trim();
            bool tableConstraint = StartsWithKeyword(trimmed, "CONSTRAINT") || StartsWithKeyword(trimmed, "CHECK") || StartsWithKeyword(trimmed, "PRIMARY")
                || StartsWithKeyword(trimmed, "UNIQUE") || StartsWithKeyword(trimmed, "FOREIGN");
            string? column = tableConstraint ? null : FirstIdentifier(trimmed);
            foreach (string expression in FindChecks(trimmed))
                checks.Add(new CheckConstraint(column, expression));
        }

        return new TableDefinition(checks, withoutRowId, strict);
    }

    private static bool StartsWithKeyword(string text, string keyword) =>
        text.StartsWithIgnoreCase(keyword) && (text.Length == keyword.Length || !char.IsLetterOrDigit(text[keyword.Length]) && text[keyword.Length] != '_');

    /// <summary> The name a column definition starts with, unquoted. </summary>
    private static string? FirstIdentifier(string segment)
    {
        if (segment.Length == 0)
            return null;
        char first = segment[0];
        char? closing = first switch { '"' => '"', '`' => '`', '[' => ']', '\'' => '\'', _ => null };
        if (closing is { } end)
        {
            var name = new StringBuilder();
            for (int i = 1; i < segment.Length; i++)
            {
                if (segment[i] == end)
                {
                    if (end != ']' && i + 1 < segment.Length && segment[i + 1] == end)
                    {
                        name.Append(end);
                        i++;
                        continue;
                    }
                    return name.ToString();
                }
                name.Append(segment[i]);
            }
            return name.ToString();
        }

        int stop = 0;
        while (stop < segment.Length && !char.IsWhiteSpace(segment[stop]))
            stop++;
        return segment[..stop];
    }

    /// <summary> The segments of a column list separated by commas that are not inside parentheses, quotes or comments. </summary>
    private static List<string> SplitTopLevel(string text)
    {
        var segments = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int skipped = SkipQuotedOrComment(text, i);
            if (skipped > i)
            {
                i = skipped - 1;
                continue;
            }
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (text[i] == ',' && depth == 0)
            {
                segments.Add(text[start..i]);
                start = i + 1;
            }
        }
        segments.Add(text[start..]);
        return segments;
    }

    /// <summary> The expressions of every <c>CHECK (...)</c> in a segment. </summary>
    private static List<string> FindChecks(string segment)
    {
        var found = new List<string>();
        for (int i = 0; i < segment.Length; i++)
        {
            int skipped = SkipQuotedOrComment(segment, i);
            if (skipped > i)
            {
                i = skipped - 1;
                continue;
            }
            bool wordStart = i == 0 || !(char.IsLetterOrDigit(segment[i - 1]) || segment[i - 1] == '_');
            if (!wordStart || string.Compare(segment, i, "CHECK", 0, 5, StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            int j = i + 5;
            while (j < segment.Length && char.IsWhiteSpace(segment[j]))
                j++;
            if (j >= segment.Length || segment[j] != '(')
                continue;
            int end = MatchingParenthesis(segment, j);
            if (end < 0)
                continue;
            found.Add(segment[(j + 1)..end].Trim());
            i = end;
        }
        return found;
    }

    private static int IndexOfOutsideQuotes(string text, char wanted, int from)
    {
        for (int i = from; i < text.Length; i++)
        {
            int skipped = SkipQuotedOrComment(text, i);
            if (skipped > i)
            {
                i = skipped - 1;
                continue;
            }
            if (text[i] == wanted)
                return i;
        }
        return -1;
    }

    private static int MatchingParenthesis(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            int skipped = SkipQuotedOrComment(text, i);
            if (skipped > i)
            {
                i = skipped - 1;
                continue;
            }
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary> When a quoted text, a quoted identifier or a comment starts at <paramref name="i"/>, the index just after it; otherwise <paramref name="i"/>. </summary>
    private static int SkipQuotedOrComment(string text, int i)
    {
        char c = text[i];
        if (c is '\'' or '"' or '`')
        {
            for (int j = i + 1; j < text.Length; j++)
            {
                if (text[j] != c)
                    continue;
                if (j + 1 < text.Length && text[j + 1] == c)
                {
                    j++;   // a doubled quote is a quote character inside the text
                    continue;
                }
                return j + 1;
            }
            return text.Length;
        }
        if (c == '[')
        {
            int j = text.IndexOf(']', i + 1);
            return j < 0 ? text.Length : j + 1;
        }
        if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
        {
            int j = text.IndexOf('\n', i);
            return j < 0 ? text.Length : j + 1;
        }
        if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
        {
            int j = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
            return j < 0 ? text.Length : j + 2;
        }
        return i;
    }
}
