namespace CodeGenNew.Core;

public enum SettingKind
{
    Text,
    Boolean,
    /// <summary> One of up to three values, shown as radio buttons. </summary>
    Radio,
    /// <summary> One of more than three values, shown as a drop-down. </summary>
    Dropdown,
    /// <summary> Any of the values, shown as check boxes and stored comma-separated. </summary>
    Multi,
    /// <summary> A whole number within a range, shown as a number box. Blank stays possible: it means the default. </summary>
    Number
}

/// <summary> One valid value of a setting and the words the settings screen shows for it. </summary>
public sealed record SettingChoice(string Value, string Label);

/// <summary> The settings whose value comes from a short fixed list, so the settings screen offers the list instead of a box to type in. A single-choice setting starts with a
/// blank choice that stands for "not set" (the default). </summary>
public static class ProjectSettingChoices
{
    private static SettingChoice NotSet(string meaning) => new("", $"Not set ({meaning})");

    /// <summary> The stacks a project can generate; the same list as ProjectPlan.KnownStacks (a test keeps them equal). </summary>
    public static readonly string[] Stacks = ["Api", "WinUI3", "React", "Angular", "Blazor", "Rust", "Python"];

    public const int HighestPort = 65535;

    /// <summary> The settings that are whole numbers, with the lowest and highest value the settings screen accepts. </summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> Numbers =
        new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["MinYear"] = (1, 9999),
            ["MaxYear"] = (1, 9999),
            ["EnumMaxRows"] = (1, 100000),
            ["AngularVersion"] = (1, 99),
            ["ApiPort"] = (1, HighestPort),
            ["DevPort"] = (1, HighestPort),
            ["RustPort"] = (1, HighestPort),
        };

    public static readonly IReadOnlyDictionary<string, (SettingKind Kind, SettingChoice[] Choices)> All =
        new Dictionary<string, (SettingKind, SettingChoice[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stacks"] = (SettingKind.Multi, Stacks.Select(s => new SettingChoice(s, s)).ToArray()),
            ["DatabaseProvider"] = (SettingKind.Dropdown,
            [
                NotSet("SQL Server"), new("SqlServer", "SQL Server"), new("PostgreSql", "PostgreSQL"), new("MySql", "MySQL"), new("Sqlite", "SQLite")
            ]),
            ["AccessMode"] = (SettingKind.Radio, [NotSet("Routines"), new("Routines", "Routines"), new("Ef", "Ef")]),
            ["NamingStyle"] = (SettingKind.Radio, [NotSet("as is"), new("AsIs", "As is"), new("Pascal", "Pascal")]),
            ["DbSetNames"] = (SettingKind.Radio, [NotSet("the table name"), new("Plural", "Plural")]),
        };
}
