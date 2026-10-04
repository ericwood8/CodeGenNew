using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> The limits a CHECK constraint puts on one numeric column: <c>CHECK (credit_limit &gt;= 0)</c> is a minimum of 0, <c>CHECK (rating BETWEEN 1 AND 5)</c> is 1 to 5.
/// A strict bound (<c>&gt; 0</c>) is kept as strict: a whole-number column turns it into the next whole number, a decimal box treats it as inclusive (the database still refuses the edge). </summary>
public sealed record CheckRange(double? Min, bool MinStrict, double? Max, bool MaxStrict)
{
    /// <summary> The lowest value an integer column accepts under this check, or null for no lower limit. </summary>
    public long? IntegerMin => Min is null ? null : (long)(MinStrict ? Math.Floor(Min.Value) + 1 : Math.Ceiling(Min.Value));

    /// <summary> The highest value an integer column accepts under this check, or null for no upper limit. </summary>
    public long? IntegerMax => Max is null ? null : (long)(MaxStrict ? Math.Ceiling(Max.Value) - 1 : Math.Floor(Max.Value));

    /// <summary> Both checks together: the stricter lower and upper limit of each. </summary>
    public CheckRange Combine(CheckRange other)
    {
        double? min = Min, max = Max;
        bool minStrict = MinStrict, maxStrict = MaxStrict;
        if (other.Min is { } om && (min is null || om > min || (om == min && other.MinStrict))) { min = om; minStrict = other.MinStrict; }
        if (other.Max is { } ox && (max is null || ox < max || (ox == max && other.MaxStrict))) { max = ox; maxStrict = other.MaxStrict; }
        return new CheckRange(min, minStrict, max, maxStrict);
    }
}

/// <summary> Reads what a database says about one column in a CHECK constraint, in the three spellings the databases store it: SQL Server <c>([Rating]&gt;=(1) AND [Rating]&lt;=(5))</c> and
/// <c>([Status]='Open' OR [Status]='Closed')</c> (an <c>IN</c> list is stored as that chain), PostgreSQL <c>CHECK (((points &gt;= 0) AND (points &lt;= 10)))</c> and
/// <c>= ANY (ARRAY[...])</c>, MySQL <c>(`rating` between 1 and 5)</c> and <c>(`status` in (_utf8mb4'Open',_utf8mb4'Closed'))</c>. Only plain shapes are understood (a column against number
/// literals joined by AND, or a column against string literals): an OR of ranges, a function, another column or anything else gives null and the column stays as the name rules say. </summary>
public static partial class CheckConstraintParser
{
    private static readonly Regex StringLiteral = new("N?'((?:[^']|'')*)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Cast = new(@"::[a-z_][a-z0-9_ ]*(\[\])?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Conjunct = new(@"^(?:(?<col>[a-z0-9_$ ]+?)\s*(?<op>>=|<=|<>|!=|>|<|=)\s*(?<num>-?\d+(?:\.\d+)?)|(?<num2>-?\d+(?:\.\d+)?)\s*(?<op2>>=|<=|>|<|=)\s*(?<col2>[a-z0-9_$ ]+?))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Between = new(@"(?<col>[a-z0-9_$]+)\s+between\s+(?<lo>-?\d+(?:\.\d+)?)\s+and\s+(?<hi>-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary> The names a constraint definition mentions as quoted identifiers (<c>[Name]</c>, <c>`name`</c>, <c>"name"</c>), without duplicates. A constraint that names exactly one
    /// column is a column's own check, whichever way it was written; null otherwise. </summary>
    public static string? SingleColumn(string definition)
    {
        var withoutLiterals = StringLiteral.Replace(definition, "''");
        var names = Regex.Matches(withoutLiterals, @"\[([^\]]+)\]|`([^`]+)`|""([^""]+)""")
            .Select(m => (m.Groups[1].Success ? m.Groups[1] : m.Groups[2].Success ? m.Groups[2] : m.Groups[3]).Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return names.Count == 1 ? names[0] : null;
    }

    /// <summary> The limits <paramref name="definition"/> puts on <paramref name="column"/>, or null when it is not a plain range check on that column. </summary>
    public static CheckRange? ParseRange(string definition, string column)
    {
        if (definition.Contains('\'') || Regex.IsMatch(definition, @"\bOR\b", RegexOptions.IgnoreCase))
            return null;   // a string comparison, or a choice of alternatives, is not a range

        string text = Cast.Replace(definition, "");
        text = Regex.Replace(text, @"^\s*check\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[\[\]`""]", "");
        text = Regex.Replace(text, @"[()]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();
        text = Between.Replace(text, m => $"{m.Groups["col"].Value} >= {m.Groups["lo"].Value} and {m.Groups["col"].Value} <= {m.Groups["hi"].Value}");

        string wanted = column.ToLowerInvariant();
        double? min = null, max = null;
        bool minStrict = false, maxStrict = false, any = false;
        foreach (string part in text.Split(" and ", StringSplitOptions.TrimEntries))
        {
            var match = Conjunct.Match(part);
            if (!match.Success)
                return null;
            bool columnFirst = match.Groups["col"].Success;
            string name = (columnFirst ? match.Groups["col"] : match.Groups["col2"]).Value.Trim();
            string op = (columnFirst ? match.Groups["op"] : match.Groups["op2"]).Value;
            double number = double.Parse((columnFirst ? match.Groups["num"] : match.Groups["num2"]).Value, CultureInfo.InvariantCulture);
            if (name != wanted || op is "<>" or "!=")
                return null;
            if (!columnFirst)   // "0 <= col" says the same as "col >= 0"
                op = op switch { "<=" => ">=", ">=" => "<=", "<" => ">", ">" => "<", _ => op };

            any = true;
            switch (op)
            {
                case ">=": case ">": Tighten(ref min, ref minStrict, number, op == ">", lower: true); break;
                case "<=": case "<": Tighten(ref max, ref maxStrict, number, op == "<", lower: false); break;
                case "=": min = max = number; minStrict = maxStrict = false; break;
            }
        }
        return any ? new CheckRange(min, minStrict, max, maxStrict) : null;
    }

    private static void Tighten(ref double? bound, ref bool strict, double number, bool isStrict, bool lower)
    {
        bool tighter = bound is null || (lower ? number > bound : number < bound) || (number == bound && isStrict);
        if (!tighter)
            return;
        bound = number;
        strict = isStrict;
    }

    /// <summary> The values <paramref name="definition"/> lists for <paramref name="column"/> (<c>[Status]='Open' OR [Status]='Closed'</c>, or <c>`status` in ('Open','Closed')</c>), or null when
    /// it is not exactly such a list. </summary>
    public static List<string>? ParseList(string definition, string column)
    {
        definition = definition.Replace("\\'", "'");   // MySQL stores the quotes of a literal escaped
        string name = Regex.Escape(column);
        string identifier = $@"(?:\[{name}\]|`{name}`|""{name}""|\b{name}\b)";

        // an OR chain: col = 'a' OR col = 'b'
        var equals = new Regex($@"\(*\s*{identifier}\s*=\s*(?:_[a-z0-9]+)?N?'((?:[^']|'')*)'\s*\)*", RegexOptions.IgnoreCase);
        var matches = equals.Matches(definition);
        if (matches.Count > 0)
        {
            string rest = equals.Replace(definition, " ");
            rest = Regex.Replace(rest, @"\bor\b|\bcheck\b|[()\s]", "", RegexOptions.IgnoreCase);
            if (rest.Length == 0)
                return matches.Select(m => m.Groups[1].Value.Replace("''", "'")).ToList();
        }

        // an IN list: col in ('a', 'b')
        var inList = Regex.Match(definition, $@"^\s*(?:check\s*)?\(*\s*{identifier}\s+in\s*\((?<list>.*)\)\s*\)*\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (inList.Success)
        {
            string list = inList.Groups["list"].Value;
            var values = Regex.Matches(list, @"(?:_[a-z0-9]+)?N?'((?:[^']|'')*)'", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value.Replace("''", "'")).ToList();
            string rest = Regex.Replace(list, @"(?:_[a-z0-9]+)?N?'(?:[^']|'')*'", "", RegexOptions.IgnoreCase);
            if (values.Count > 0 && Regex.Replace(rest, @"[,\s()]", "").Length == 0)
                return values;
        }
        return null;
    }
}
