using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> One column whose stored values break a rule the generated validators enforce: a form would refuse to save such a row until the value is changed. </summary>
/// <param name="Rule"> What the value must be, for example "a postal code" or "at most 100 characters". </param>
/// <param name="Examples"> A few of the failing values, shortest first. </param>
public sealed record DataFinding(string Table, string Column, string Rule, int Count, int Total, IReadOnlyList<string> Examples);

/// <summary> Reads the rows of a table (<c>TableModel.Rows</c>) against the rules of <see cref="ColumnRules.RuleOf"/>: the email, phone, URL, postal code and social security shapes, the length, the allowed
/// values, the number ranges and the "never later than today" dates. A row that fails is one the generated forms and APIs would refuse to save after an unrelated edit, so the project either
/// fixes the value or lists the column in <c>PlainTextColumns</c>. </summary>
public static class DataCheck
{
    private const int ExamplesPerFinding = 3;

    public static IReadOnlyList<DataFinding> Of(TableModel table, ProjectSettings project)
    {
        var findings = new List<DataFinding>();
        foreach (var column in table.Columns)
        {
            if (column.RuleOf(project, table.TableName) is not { } rule)
                continue;

            int index = table.Columns.IndexOf(column);
            var values = table.Rows.Select(row => row[index]).ToList();

            foreach (var (name, fails) in Checks(column, rule))
            {
                var failing = values.Where(v => v is not null and not DBNull && fails(v)).Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList();
                if (failing.Count > 0)
                    findings.Add(new DataFinding(table.TableName, column.Name, name, failing.Count, values.Count, failing.OrderBy(v => v.Length).Distinct().Take(ExamplesPerFinding).ToList()));
            }
        }
        foreach (var pair in DatePairs.For(table, project))
        {
            int startIndex = table.Columns.IndexOf(pair.Start), endIndex = table.Columns.IndexOf(pair.End);
            var failing = table.Rows.Where(row => row[startIndex] is DateTime start && row[endIndex] is DateTime end && end.Date < start.Date)
                .Select(row => ((DateTime)row[startIndex]!).ToString("yyyy-MM-dd") + " to " + ((DateTime)row[endIndex]!).ToString("yyyy-MM-dd")).ToList();
            if (failing.Count > 0)
                findings.Add(new DataFinding(table.TableName, pair.End.Name, "a date not earlier than " + pair.Start.Name, failing.Count, table.Rows.Count, failing.Distinct().Take(ExamplesPerFinding).ToList()));
        }
        return findings;
    }

    private static IEnumerable<(string Name, Func<object, bool> Fails)> Checks(ColumnModel column, ColumnRule rule)
    {
        if (column.IsStringColumn)
        {
            if (rule.MaxLength > 0)
                yield return ($"at most {rule.MaxLength} characters long", v => v is string s && s.Length > rule.MaxLength);
            if (rule.Choices is { Count: > 0 } choices)
                yield return ("one of: " + string.Join(", ", choices), v => v is string s && s.Length > 0 && !choices.Contains(s));
            if (rule.Shape != TextShape.None)
                yield return (ColumnRules.NounOf(rule), v => v is string s && s.Trim().Length > 0 && !HasShape(rule, s));
        }
        else if (rule.HasRange)
        {
            if (rule.Min is { } min)
                yield return ((rule.MinStrict ? "more than " : "at least ") + TemplateHelpers.Num(min), v => AsNumber(v) is { } n && (rule.MinStrict ? n <= min : n < min));
            if (rule.Max is { } max)
                yield return ((rule.MaxStrict ? "less than " : "at most ") + TemplateHelpers.Num(max), v => AsNumber(v) is { } n && (rule.MaxStrict ? n >= max : n > max));
        }

        if (rule.NotFuture)
            yield return ("a date not later than today", v => v is DateTime d && d.Date > DateTime.Today);
    }

    private static double? AsNumber(object value) => value switch
    {
        string or DateTime or bool => null,
        IConvertible c => c.ToDouble(System.Globalization.CultureInfo.InvariantCulture),
        _ => null
    };

    /// <summary> Whether a stored text has the shape, by the same test the generated C# validator and attributes make. </summary>
    public static bool HasShape(ColumnRule rule, string value) => rule.Shape switch
    {
        TextShape.Email => new EmailAddressAttribute().IsValid(value),
        TextShape.Url => Uri.IsWellFormedUriString(value, UriKind.Absolute),
        _ => ColumnRules.PatternOf(rule) is { } pattern && Regex.IsMatch(value, "^(?:" + pattern + ")$")
    };
}
