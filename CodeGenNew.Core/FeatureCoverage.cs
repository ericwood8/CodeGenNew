using System.Text;

namespace CodeGenNew.Core;

/// <summary> Which stacks a project's features reach, read from the templates the project adds to its plan (<see cref="ProjectSettings.ImpliedPlanTemplates"/>), so a stack that lacks a feature is seen
/// in one table and not by a build that finds it. A cell is <c>S</c> (a template of the stack serves the feature), <c>T</c> (and a test template), or <c>.</c> (none). </summary>
public static class FeatureCoverage
{
    public static IReadOnlyList<string> Stacks { get; } = ["API", "Python", "Rust", "React", "Angular", "Blazor", "Uno", "WinUI3"];

    private static readonly Dictionary<string, string> StackOfPrefix = new(StringComparer.Ordinal)
    {
        ["API"] = "API", ["PY"] = "Python", ["RS"] = "Rust", ["TSX"] = "React", ["TS"] = "Angular", ["BLZ"] = "Blazor", ["UNO"] = "Uno", ["WinUI3"] = "WinUI3"
    };

    public sealed record Row(string Feature, IReadOnlyDictionary<string, string> Cells);

    /// <summary> The stack and feature of a template name (<c>PY_TestSubscriptions</c> is the Python tests of Subscription); null for a template no stack owns (<c>SP_</c>, <c>CS_</c>, <c>MD_</c>). </summary>
    public static (string Stack, string Feature, bool IsTest)? Classify(string template)
    {
        int underscore = template.IndexOf('_');
        if (underscore <= 0 || !StackOfPrefix.TryGetValue(template[..underscore], out string? stack))
            return null;
        string feature = template[(underscore + 1)..];
        bool isTest = false;
        if (feature.StartsWith("Test", StringComparison.Ordinal))
        {
            isTest = true;
            feature = feature[4..];
        }
        else if (feature.EndsWith("Test", StringComparison.Ordinal) && feature.Length > 4)
        {
            isTest = true;
            feature = feature[..^4];
        }
        if (feature.Length > 4 && feature.EndsWith("Page", StringComparison.Ordinal))
            feature = feature[..^4];
        if (feature is "" or "Bodies")
            feature = "Crud";
        if (feature.Length > 3 && feature.EndsWith('s') && !feature.EndsWith("ss", StringComparison.Ordinal))
            feature = feature[..^1];
        return (stack, feature, isTest);
    }

    public static IReadOnlyList<Row> Rows(ProjectSettings project)
    {
        var cells = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string template in project.ImpliedPlanTemplates.Distinct())
        {
            if (Classify(template) is not { } found)
                continue;
            if (!cells.TryGetValue(found.Feature, out var row))
                cells[found.Feature] = row = Stacks.ToDictionary(s => s, _ => ".");
            if (found.IsTest)
                row[found.Stack] = "T";
            else if (row[found.Stack] != "T")
                row[found.Stack] = "S";
        }
        return cells.Select(c => new Row(c.Key, c.Value)).ToList();
    }

    /// <summary> The table as text, one line per feature; a feature that some stack serves and another does not is marked with <c>&lt;</c>. </summary>
    public static string Text(ProjectSettings project)
    {
        var rows = Rows(project);
        if (rows.Count == 0)
            return "The project names no feature that a stack serves.\n";
        int width = Math.Max(7, rows.Max(r => r.Feature.Length));
        var o = new StringBuilder();
        o.Append("Feature".PadRight(width)).Append(' ').AppendJoin(' ', Stacks.Select(s => s.PadRight(7))).Append('\n');
        foreach (var row in rows)
        {
            o.Append(row.Feature.PadRight(width)).Append(' ').AppendJoin(' ', Stacks.Select(s => row.Cells[s].PadRight(7)));
            if (row.Cells.Values.Any(v => v != ".") && row.Cells.Values.Any(v => v == "."))
                o.Append(" <");
            o.Append('\n');
        }
        o.Append("S: served, T: served with tests, .: none. A feature that only some stacks serve ends with <.\n");
        return o.ToString();
    }
}
