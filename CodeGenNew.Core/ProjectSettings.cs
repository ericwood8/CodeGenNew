namespace CodeGenNew.Core;

/// <summary>
/// Per-project generation settings: the namespaces, context name, folders and table lists a generated file needs
/// that belong to the TARGET project rather than to the table being generated. Stored as one
/// <c>&lt;ProjectName&gt;.config</c> file per project (key=value lines, # comments, comma-separated lists) and
/// handed to every template as its second parameter, <c>Project</c>. Values are written into the generated text as
/// literals at generation time -- the compiler never sees this file.
///
/// Resolution order for each value: an explicit value (a command-line override, else the file) wins; otherwise a
/// convention derived from <see cref="ProjectName"/>; otherwise null, which tells the template to use its own
/// built-in literal. <see cref="None"/> (no project chosen) therefore returns null everywhere except the year range,
/// so a template run with no project generates exactly as it did before project settings existed.
/// A list-valued setting is the one exception to "null means template default": once a project IS chosen and does
/// not list one, it is empty rather than the template's built-in list, because those built-in lists name another
/// project's tables.
/// </summary>
public class ProjectSettings
{
    public const int DefaultMinYear = 2000;
    public const int DefaultMaxYear = 2100;

    /// <summary> The recognized keys, in the order the settings screen shows them. </summary>
    public static readonly IReadOnlyList<string> Keys =
    [
        "ProjectName", "ViewNamespace", "ViewModelNamespace", "ContextName", "ContextNamespace", "ApiNamespace",
        "EnumNamespace", "RepoNamespace", "EntityNamespace", "MinYear", "MaxYear", "ViewsFolder", "ViewModelsFolder",
        "Usings", "DetailMasterTables", "NoLookupParents", "NoRepositoryTables", "NoApiTables", "NoNavigationTables"
    ];

    private readonly Dictionary<string, string> _values;

    private ProjectSettings(Dictionary<string, string> values) => _values = values;

    /// <summary> No project chosen: every value falls back to the template's own literal. </summary>
    public static ProjectSettings None { get; } = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary> The raw explicit values, for the settings screen to show and edit. </summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    public static ProjectSettings FromValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                dictionary[key.Trim()] = value.Trim();
        }
        return new ProjectSettings(dictionary);
    }

    /// <summary> Parses key=value text; blank lines and lines starting with # are skipped. </summary>
    public static ProjectSettings Parse(string text)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            pairs.Add(new(line[..equals], line[(equals + 1)..]));
        }
        return FromValues(pairs);
    }

    public static ProjectSettings Load(string path) => Parse(File.ReadAllText(path));

    public static string FilePathFor(string projectsDirectory, string projectName) =>
        Path.Combine(projectsDirectory, projectName + ".config");

    /// <summary> Names of every project that has a config file in <paramref name="projectsDirectory"/>. </summary>
    public static IReadOnlyList<string> ListProjects(string projectsDirectory) =>
        Directory.Exists(projectsDirectory)
            ? Directory.EnumerateFiles(projectsDirectory, "*.config")
                .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary> Loads the named project's file, or throws <see cref="FileNotFoundException"/> when there is none. </summary>
    public static ProjectSettings LoadNamed(string projectsDirectory, string projectName)
    {
        string path = FilePathFor(projectsDirectory, projectName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No project settings file '{path}'.", path);
        var loaded = Load(path);
        // The file name is the project's identity, so a file that omits ProjectName still gets one.
        return loaded.ProjectName is null ? loaded.WithOverrides([new("ProjectName", projectName)]) : loaded;
    }

    /// <summary> A copy with <paramref name="overrides"/> taking precedence (command-line flags over the file). </summary>
    public ProjectSettings WithOverrides(IEnumerable<KeyValuePair<string, string>> overrides)
    {
        var merged = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
        {
            if (!string.IsNullOrWhiteSpace(value))
                merged[key.Trim()] = value.Trim();
        }
        return new ProjectSettings(merged);
    }

    /// <summary> Serializes the explicit values back to file text, with a short header. </summary>
    public string ToFileText()
    {
        var lines = new List<string>
        {
            "# Project settings for CodeGenNew: one project per file, named <ProjectName>.config.",
            "# key=value; # starts a comment; list values are comma-separated. Only ProjectName is required --",
            "# any namespace left out is derived from it (e.g. <ProjectName>.App.Views)."
        };
        foreach (string key in Keys)
        {
            if (_values.TryGetValue(key, out string? value))
                lines.Add($"{key}={value}");
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private string? Explicit(string key) => _values.TryGetValue(key, out string? value) ? value : null;

    /// <summary> Explicit value, else "<ProjectName><suffix>" when a project name is known, else null. </summary>
    private string? Derived(string key, string suffix) =>
        Explicit(key) ?? (ProjectName is { } name ? name + suffix : null);

    private string[]? List(string key) =>
        Explicit(key) is { } text
            ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : ProjectName is null ? null : [];

    public string? ProjectName => Explicit("ProjectName");

    public string? ViewNamespace => Derived("ViewNamespace", ".App.Views");
    public string? ViewModelNamespace => Derived("ViewModelNamespace", ".App.ViewModels");
    public string? ContextName => Derived("ContextName", "Context");
    public string? ContextNamespace => Derived("ContextNamespace", ".App.Data");
    public string? ApiNamespace => Derived("ApiNamespace", ".ApiService.Apis");
    public string? EnumNamespace => Derived("EnumNamespace", ".App.Enums");
    public string? RepoNamespace => Derived("RepoNamespace", ".App.Repositories");
    public string? EntityNamespace => Derived("EntityNamespace", ".App.Entities");

    public int MinYear => int.TryParse(Explicit("MinYear"), out int year) ? year : DefaultMinYear;
    public int MaxYear => int.TryParse(Explicit("MaxYear"), out int year) ? year : DefaultMaxYear;

    public string? ViewsFolder => Explicit("ViewsFolder");
    public string? ViewModelsFolder => Explicit("ViewModelsFolder");

    /// <summary> Extra namespaces every generated file must "using". Null (no project) keeps the template's own list. </summary>
    public string[]? Usings => List("Usings");
    /// <summary> Tables whose Add/Edit dialog is a <Table>DetailMasterDialog (WinUI3_DetailMasterScreen) instead of the
    /// plain <Table>DetailDialog: a list screen and a parent's child grid open the one that exists. </summary>
    public string[]? DetailMasterTables => List("DetailMasterTables");
    public string[]? NoLookupParents => List("NoLookupParents");
    public string[]? NoRepositoryTables => List("NoRepositoryTables");
    public string[]? NoApiTables => List("NoApiTables");
    public string[]? NoNavigationTables => List("NoNavigationTables");
}
