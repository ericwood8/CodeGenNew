using System.Text;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> What a CHECK constraint says about the text of one column: its length (<c>CHECK (len(code) = 5)</c> on SQL Server, <c>char_length(code)</c> on MySQL, <c>length(code)</c> on PostgreSQL, alone or between two limits)
/// or its shape (<c>CHECK (code LIKE 'AB%')</c>; PostgreSQL stores a LIKE as <c>~~</c>). Only these plain shapes joined by AND are understood; anything else gives null and the column stays free text. The answer is a
/// <see cref="TextNotation"/>, so every validator, sample value and test helper that already holds a column to a notation holds it to its check. </summary>
public static class CheckTextParser
{
    private static readonly Regex Literal = new(@"(?:_[a-z0-9]+)?N?'((?:[^']|'')*)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Cast = new(@"::[a-z_][a-z0-9_ ]*(\[\])?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Length = new(@"^(?:len|length|char_length|character_length)\s+(?<col>[a-z0-9_$ ]+?)\s*(?<op>>=|<=|>|<|=)\s*(?<n>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Like = new(@"^(?<col>[a-z0-9_$ ]+?)\s+(?:like|~~)\s+@(?<i>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary> The notation <paramref name="definition"/> puts on <paramref name="column"/>, or null when it is not a plain length or LIKE check on that column. </summary>
    public static TextNotation? Parse(string definition, string column)
    {
        var literals = new List<string>();
        string text = Literal.Replace(definition.Replace("\\'", "'"), m => { literals.Add(m.Groups[1].Value.Replace("''", "'")); return "@" + (literals.Count - 1); });
        text = Cast.Replace(text, "");
        text = Regex.Replace(text, @"^\s*check\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[\[\]`""]", "");
        text = Regex.Replace(text, @"[()]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();

        int? min = null, max = null;
        string? like = null;
        foreach (string part in Regex.Split(text, @"\s+and\s+", RegexOptions.IgnoreCase))
        {
            var length = Length.Match(part);
            if (length.Success && length.Groups["col"].Value.Trim().Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                int n = int.Parse(length.Groups["n"].Value);
                switch (length.Groups["op"].Value)
                {
                    case "=": min = Math.Max(min ?? 0, n); max = Math.Min(max ?? int.MaxValue, n); break;
                    case ">=": min = Math.Max(min ?? 0, n); break;
                    case ">": min = Math.Max(min ?? 0, n + 1); break;
                    case "<=": max = Math.Min(max ?? int.MaxValue, n); break;
                    default: max = Math.Min(max ?? int.MaxValue, n - 1); break;
                }
                continue;
            }
            var like2 = Like.Match(part);
            if (like2.Success && like2.Groups["col"].Value.Trim().Equals(column, StringComparison.OrdinalIgnoreCase) && like is null)
            {
                like = literals[int.Parse(like2.Groups["i"].Value)];
                continue;
            }
            return null;
        }

        if (like is not null)
            return FromLike(like);
        if (min is null && max is null)
            return null;
        int low = Math.Max(min ?? 0, 0);
        if (max is { } high && high < low)
            return null;
        string any = @"[\s\S]";
        string pattern = max is { } top ? (low == top ? $"{any}{{{low}}}" : $"{any}{{{low},{top}}}") : $"{any}{{{low},}}";
        string noun = max is { } m2 ? (low == m2 ? $"text of {low} characters" : low == 0 ? $"text of at most {m2} characters" : $"text of {low} to {m2} characters") : $"text of at least {low} characters";
        return new TextNotation(pattern, noun, new string('A', Math.Max(low, 1)));
    }

    /// <summary> The notation of <c>LIKE 'AB%'</c>: % is any text, _ is any one character, anything else is itself. A pattern with a bracketed class (SQL Server) is not understood. </summary>
    private static TextNotation? FromLike(string like)
    {
        if (like.Contains('[') || like.Contains('\\'))
            return null;
        var pattern = new StringBuilder();
        var sample = new StringBuilder();
        foreach (char c in like)
        {
            switch (c)
            {
                case '%': pattern.Append(@"[\s\S]*"); break;
                case '_': pattern.Append(@"[\s\S]"); sample.Append('A'); break;
                default:
                    // the databases compare a LIKE without regard to case (their usual collations), so a letter is either case: the database has the last word
                    pattern.Append(char.IsLetter(c) && char.ToUpperInvariant(c) != char.ToLowerInvariant(c) ? $"[{char.ToUpperInvariant(c)}{char.ToLowerInvariant(c)}]"
                        : @"\.^$|?*+()[]{}".Contains(c) ? "\\" + c : c.ToString());
                    sample.Append(c);
                    break;
            }
        }
        string noun = $"text that matches {like.Replace("{", "").Replace("}", "")} (% is any text, _ any one character)";
        return new TextNotation(pattern.ToString(), noun, sample.ToString());
    }
}
