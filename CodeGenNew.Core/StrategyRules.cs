namespace CodeGenNew.Core;

public enum StrategyUi { WinUI3, React, Angular, Blazor, Uno }

/// <summary> How the API reaches the database. <see cref="OwnSql"/> is what the Rust and Python APIs do: they write the SQL themselves, with no routine and no EF context. </summary>
public enum StrategyAccess { Routines, Ef, OwnSql }

public enum StrategyStyle { Rest, OData }

public enum StrategyLanguage { CSharp, Rust, Python }

/// <summary> The five decisions that decide a project: the database it reads, and the UI, API access mode, API style and API language the person picks. A field that is not picked yet is null. </summary>
public sealed record StrategyChoice(SqlDialect Database, StrategyUi? Ui = null, StrategyAccess? Access = null, StrategyStyle? Style = null, StrategyLanguage? Language = null);

/// <summary> One value of a drop-down: <see cref="WhyNot"/> is the reason an earlier pick rules it out, or null when it is offered. </summary>
public sealed record StrategyOption<T>(T Value, string Label, string? WhyNot) where T : struct
{
    public bool IsOffered => WhyNot is null;
}

/// <summary> Which combinations of the five choices make a project, and the project settings a combination stands for. Each drop-down is narrowed by the picks before it (the UI, then the access
/// mode, the style and the language), so a person never reaches a combination the rules refuse. The Generate All dialog and <c>codegen generate</c> both go through here. </summary>
public static class StrategyRules
{
    public const string WinUi3NeedsNoApi = "WinUI3 reads the database directly, so it takes no API choice.";
    public const string SqliteHasNoRoutines = "SQLite has no stored routines.";
    public const string RustAndPythonWriteOwnSql = "Rust and Python write their own SQL, so they use neither stored routines nor EF.";
    public const string CSharpUsesRoutinesOrEf = "The C# API reaches the database through stored routines or EF, not by its own SQL.";
    public const string ODataIsCSharpOnly = "OData is served by the C# API only; Rust and Python write no OData.";
    public const string RustCannotReadSqlServer = "Rust (sqlx) cannot read SQL Server.";

    public static readonly IReadOnlyList<StrategyUi> Uis = Enum.GetValues<StrategyUi>();

    /// <summary> The reason a combination is refused, or null when the picks made so far fit together. A field that is null is not judged. </summary>
    public static string? Refusal(StrategyChoice choice)
    {
        if (choice.Ui == StrategyUi.WinUI3 && (choice.Access is not null || choice.Style is not null || choice.Language is not null))
            return WinUi3NeedsNoApi;
        if (choice.Access == StrategyAccess.Routines && choice.Database == SqlDialect.Sqlite)
            return SqliteHasNoRoutines;
        if (choice.Access is StrategyAccess.Routines or StrategyAccess.Ef && choice.Language is StrategyLanguage.Rust or StrategyLanguage.Python)
            return RustAndPythonWriteOwnSql;
        if (choice.Access == StrategyAccess.OwnSql && choice.Language == StrategyLanguage.CSharp)
            return CSharpUsesRoutinesOrEf;
        if (choice.Style == StrategyStyle.OData && (choice.Language is StrategyLanguage.Rust or StrategyLanguage.Python || choice.Access == StrategyAccess.OwnSql))
            return ODataIsCSharpOnly;
        if (choice.Language == StrategyLanguage.Rust && choice.Database == SqlDialect.SqlServer)
            return RustCannotReadSqlServer;
        return null;
    }

    public static IReadOnlyList<StrategyOption<StrategyUi>> UiOptions() =>
        Uis.Select(ui => new StrategyOption<StrategyUi>(ui, Label(ui), null)).ToList();

    public static IReadOnlyList<StrategyOption<StrategyAccess>> AccessOptions(StrategyChoice choice) =>
        Enum.GetValues<StrategyAccess>().Select(access => new StrategyOption<StrategyAccess>(access, Label(access),
            Refusal(choice with { Access = access, Style = null, Language = null }))).ToList();

    public static IReadOnlyList<StrategyOption<StrategyStyle>> StyleOptions(StrategyChoice choice) =>
        Enum.GetValues<StrategyStyle>().Select(style => new StrategyOption<StrategyStyle>(style, Label(style),
            Refusal(choice with { Style = style, Language = null }))).ToList();

    public static IReadOnlyList<StrategyOption<StrategyLanguage>> LanguageOptions(StrategyChoice choice) =>
        Enum.GetValues<StrategyLanguage>().Select(language => new StrategyOption<StrategyLanguage>(language, Label(language),
            Refusal(choice with { Language = language }))).ToList();

    /// <summary> The choice with every later pick cleared that an earlier pick now rules out; picks that still fit stay. Called after a drop-down changed. </summary>
    public static StrategyChoice Narrow(StrategyChoice choice)
    {
        if (choice.Ui == StrategyUi.WinUI3)
            return choice with { Access = null, Style = null, Language = null };
        if (choice.Access is { } access && Refusal(choice with { Access = access, Style = null, Language = null }) is not null)
            choice = choice with { Access = null, Style = null, Language = null };
        if (choice.Style is { } style && Refusal(choice with { Style = style, Language = null }) is not null)
            choice = choice with { Style = null, Language = null };
        if (choice.Language is { } language && Refusal(choice with { Language = language }) is not null)
            choice = choice with { Language = null };
        return choice;
    }

    /// <summary> The names of the choices still to make, in the order the dialog shows them; empty when the combination is complete. WinUI3 needs only the UI. </summary>
    public static IReadOnlyList<string> Missing(StrategyChoice choice)
    {
        var missing = new List<string>();
        if (choice.Ui is null)
            missing.Add("UI technology");
        if (choice.Ui is null or StrategyUi.WinUI3)
            return missing;
        if (choice.Access is null)
            missing.Add("API access mode");
        if (choice.Style is null)
            missing.Add("API style");
        if (choice.Language is null)
            missing.Add("API language");
        return missing;
    }

    /// <summary> The project settings a complete, accepted combination stands for. The screens' table lists are left out on purpose: the plan's defaults then cover every table the schema holds. </summary>
    public static ProjectSettings ToSettings(StrategyChoice choice, string projectName, string databaseName, string? databaseServer = null, string? databaseUser = null)
    {
        if (Refusal(choice) is { } refusal)
            throw new InvalidOperationException(refusal);
        if (Missing(choice) is { Count: > 0 } missing)
            throw new InvalidOperationException($"Still to choose: {string.Join(", ", missing)}.");

        var stacks = new List<string>();
        if (choice.Ui == StrategyUi.WinUI3)
            stacks.Add("WinUI3");
        else
        {
            stacks.Add(choice.Ui!.Value.ToString());
            stacks.Add(choice.Language switch { StrategyLanguage.Rust => "Rust", StrategyLanguage.Python => "Python", _ => "Api" });
        }

        var values = new List<KeyValuePair<string, string>>
        {
            new("ProjectName", projectName),
            new("DatabaseProvider", choice.Database switch { SqlDialect.PostgreSql => "PostgreSql", SqlDialect.MySql => "MySql", SqlDialect.Sqlite => "Sqlite", _ => "SqlServer" }),
            new("DatabaseName", databaseName),
            new("Stacks", string.Join(",", ProjectSettingChoices.Stacks.Where(stacks.Contains)))
        };
        if (choice.Database != SqlDialect.Sqlite)
        {
            values.Add(new("DatabaseServer", databaseServer ?? ""));
            values.Add(new("DatabaseUser", databaseUser ?? ""));
        }
        // the desktop view models turn a field into a property by capitalising its first letter, so a lower-case column name breaks them
        if (choice.Ui is StrategyUi.WinUI3 or StrategyUi.Uno)
            values.Add(new("NamingStyle", "Pascal"));
        if (choice.Access is StrategyAccess.Routines or StrategyAccess.Ef)
            values.Add(new("AccessMode", choice.Access.ToString()!));
        if (choice.Style == StrategyStyle.OData)
            values.Add(new("OData", "true"));
        return ProjectSettings.FromValues(values);
    }

    public static string Label(StrategyUi ui) => ui.ToString();

    public static string Label(StrategyAccess access) => access switch
    {
        StrategyAccess.Routines => "Stored routines",
        StrategyAccess.Ef => "EF",
        _ => "Own SQL (Rust and Python)"
    };

    public static string Label(StrategyStyle style) => style == StrategyStyle.OData ? "OData" : "REST";

    public static string Label(StrategyLanguage language) => language == StrategyLanguage.CSharp ? "C#" : language.ToString();

    public static bool TryParseUi(string text, out StrategyUi ui) => Enum.TryParse(text.Trim(), ignoreCase: true, out ui) && Enum.IsDefined(ui);

    public static bool TryParseAccess(string text, out StrategyAccess access)
    {
        string key = text.Trim().Replace("-", "").Replace("_", "").Replace(" ", "");
        access = key.ToLowerInvariant() switch
        {
            "routines" or "storedroutines" => StrategyAccess.Routines,
            "ef" => StrategyAccess.Ef,
            "ownsql" or "sql" => StrategyAccess.OwnSql,
            _ => (StrategyAccess)(-1)
        };
        return Enum.IsDefined(access);
    }

    public static bool TryParseStyle(string text, out StrategyStyle style)
    {
        style = text.Trim().ToLowerInvariant() switch { "rest" => StrategyStyle.Rest, "odata" => StrategyStyle.OData, _ => (StrategyStyle)(-1) };
        return Enum.IsDefined(style);
    }

    public static bool TryParseLanguage(string text, out StrategyLanguage language)
    {
        language = text.Trim().ToLowerInvariant() switch
        {
            "c#" or "csharp" or "cs" => StrategyLanguage.CSharp,
            "rust" => StrategyLanguage.Rust,
            "python" or "py" => StrategyLanguage.Python,
            _ => (StrategyLanguage)(-1)
        };
        return Enum.IsDefined(language);
    }
}
