namespace CodeGenNew.SchemaIntrospection;

/// <summary> One row of SpecialLogicColumns.config (Docs/specs.md section 5.2). </summary>
public class SpecialLogicRule
{
    public required string Category { get; init; }
    public required IReadOnlyList<string> FlagPatterns { get; init; }
    public required IReadOnlyList<string> CompanionPatterns { get; init; }
    public bool IgnoreCase { get; init; }

    /// <summary> Blank companion patterns => per-column classification rule; populated => table-level paired-column rule. </summary>
    public bool IsPairRule => CompanionPatterns.Count > 0;
}

/// <summary> Parses and evaluates SpecialLogicColumns.config. </summary>
public static class SpecialLogicColumnsConfig
{
    public static List<SpecialLogicRule> Load(string path)
    {
        var rules = new List<SpecialLogicRule>();
        if (!File.Exists(path))
            return rules;

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] parts = line.Split('|');
            if (parts.Length < 3)
                continue; // malformed line; skip rather than throw, this is a hand-editable file

            string special = parts.Length >= 4 ? parts[3] : "";
            bool ignoreCase = special.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(f => f.Equals("IgnoreCase", StringComparison.OrdinalIgnoreCase));

            rules.Add(new SpecialLogicRule
            {
                Category = parts[0].Trim(),
                FlagPatterns = SplitPatterns(parts[1]),
                CompanionPatterns = SplitPatterns(parts[2]),
                IgnoreCase = ignoreCase
            });
        }

        return rules;
    }

    private static IReadOnlyList<string> SplitPatterns(string field) =>
        field.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Pattern* -> StartsWith, *Pattern -> EndsWith, *Pattern* -> Contains, Pattern -> exact match.
    /// </summary>
    public static bool MatchesPattern(string columnName, string pattern, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool startsWithStar = pattern.StartsWith('*');
        bool endsWithStar = pattern.EndsWith('*');

        if (startsWithStar && endsWithStar && pattern.Length >= 2)
            return columnName.Contains(pattern[1..^1], comparison);
        if (endsWithStar)
            return columnName.StartsWith(pattern[..^1], comparison);
        if (startsWithStar)
            return columnName.EndsWith(pattern[1..], comparison);

        return columnName.Equals(pattern, comparison);
    }

    private static string? FindMatchingColumn(IEnumerable<string> columnNames, SpecialLogicRule rule, IReadOnlyList<string> patterns) =>
        columnNames.FirstOrDefault(name => patterns.Any(pattern => MatchesPattern(name, pattern, rule.IgnoreCase)));

    /// <summary> For a table-level pair rule, returns the matching (flagColumn, companionColumn) names if both are present. </summary>
    public static (string FlagColumn, string CompanionColumn)? EvaluatePairRule(
        SpecialLogicRule rule, IEnumerable<string> columnNames)
    {
        if (!rule.IsPairRule)
            return null;

        var names = columnNames as IList<string> ?? columnNames.ToList();
        string? flag = FindMatchingColumn(names, rule, rule.FlagPatterns);
        string? companion = FindMatchingColumn(names, rule, rule.CompanionPatterns);

        return (flag is not null && companion is not null) ? (flag, companion) : null;
    }

    /// <summary> For a per-column rule whose pattern ORDER is a priority (DisplayColumn): the index of the first pattern
    /// this name matches, or null. Lower = higher priority. </summary>
    public static int? MatchRank(SpecialLogicRule rule, string columnName)
    {
        if (rule.IsPairRule)
            return null;

        for (int i = 0; i < rule.FlagPatterns.Count; i++)
        {
            if (MatchesPattern(columnName, rule.FlagPatterns[i], rule.IgnoreCase))
                return i;
        }

        return null;
    }

    /// <summary> For a per-column classification rule (blank companion patterns), does this single column name match? </summary>
    public static bool MatchesColumnRule(SpecialLogicRule rule, string columnName)
    {
        if (rule.IsPairRule)
            return false;

        return rule.FlagPatterns.Any(pattern => MatchesPattern(columnName, pattern, rule.IgnoreCase));
    }
}
