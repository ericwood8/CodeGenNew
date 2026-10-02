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
        "EnumNamespace", "RepoNamespace", "EntityNamespace", "MinYear", "MaxYear", "ViewsFolder", "ViewModelsFolder", "CurrencyCode",
        "Usings", "DetailMasterTables", "EnumTables", "EnumMaxRows", "EnumNameSuffixes", "HiddenParents", "ModelFileOverrides", "ChildGridTitles", "BaseEntity", "BaseNameActiveEntity", "NoLookupParents", "NoRepositoryTables", "NoApiTables", "NoNavigationTables", "NamingStyle", "Acronyms", "Screens"
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

    /// <summary> How a table or column name from the database becomes the generated name: <c>NamingStyle=Pascal</c> turns <c>customer_item</c> into <c>CustomerItem</c>
    /// (the SQL keeps the real names); the default <c>AsIs</c> uses the database's names. Read by the schema reader, so it applies to every template. </summary>
    public NamingStyle Naming => Enum.TryParse<NamingStyle>(Explicit("NamingStyle"), ignoreCase: true, out var style) ? style : NamingStyle.AsIs;

    /// <summary> Words kept upper-case whole when <see cref="Naming"/> is Pascal: <c>Acronyms=PO,UPC,MSRP</c> gives <c>require_customer_po</c> -> <c>RequireCustomerPO</c>
    /// (what a SQL Server database with PascalCase names already says), so the generated names match across databases. Empty when not set. </summary>
    /// <summary> The tables that get a screen, in menu order (<c>Screens=CustomerMonthlySummary,SalesInvoice,Customer</c>). Empty when not set: the generated menu
    /// then lists every table that has an API and a search, alphabetically. </summary>
    public string[] Screens => Explicit("Screens") is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    public string[] Acronyms => Explicit("Acronyms") is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

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

    /// <summary> Tables whose foreign key columns a generated TypeScript form hides (a system or display table the
    /// person never picks from). Null: no project, the template keeps its own. A chosen project that lists none hides none. </summary>
    public string[]? HiddenParents => List("HiddenParents");

    /// <summary> Table -> TypeScript model file name, for the tables whose model file is NOT named by the usual
    /// convention (the table's base name, lower-cased, which is also what TS_Model writes). Written in the project file
    /// as <c>ModelFileOverrides=DepartmentTeam=department,ProjectTask=project</c>. Null: no project, the template keeps
    /// its own map. A chosen project that lists none uses the convention for every table. </summary>
    public Dictionary<string, string>? ModelFileOverrides
    {
        get
        {
            if (ProjectName is null)
                return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in List("ModelFileOverrides") ?? [])
            {
                int equals = pair.IndexOf('=');
                if (equals > 0 && equals < pair.Length - 1)
                    map[pair[..equals].Trim()] = pair[(equals + 1)..].Trim();
            }
            return map;
        }
    }

    /// <summary> The heading of a child grid inside a master dialog, when the table's own name does not say what the rows mean. Written in the project file as
    /// <c>ChildGridTitles=Customer.CustomerItem=Item Purchase History,Item.CustomerItem=Who Purchased?</c> (parent table, a dot, child table, an equals sign, the title):
    /// the same junction-like table reads differently under each parent. Returns the fallback when no title is set. </summary>
    public string ChildGridTitle(string parentTable, string childTable, string fallback)
    {
        foreach (string pair in List("ChildGridTitles") ?? [])
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && equals < pair.Length - 1 && pair[..equals].Trim().Equals($"{parentTable}.{childTable}", StringComparison.OrdinalIgnoreCase))
                return pair[(equals + 1)..].Trim();
        }
        return fallback;
    }

    /// <summary> Base class of a generated entity; null keeps the template's own ("BaseEntity"). </summary>
    public string? BaseEntity => Explicit("BaseEntity");
    /// <summary> Base class of a generated entity for a Name + IsActive table; null keeps the template's own. </summary>
    public string? BaseNameActiveEntity => Explicit("BaseNameActiveEntity");

    /// <summary> The same question as <see cref="IsEnumTable(string, LookupShape)"/> asked of a whole table model. </summary>
    public bool? IsEnumTable(TableModel model) => IsEnumTable(model.TableName, model.LookupShape);
    /// <summary> ...and of the table a foreign key points at. </summary>
    public bool? IsEnumTable(ForeignKeyModel foreignKey) => IsEnumTable(foreignKey.ReferencedTable, foreignKey.ReferencedLookupShape);

    /// <summary> The whole-number limits a number box for this column should enforce: what its name suggests (a year runs from
    /// MinYear to MaxYear, a month 1-12, a percentage 0-100, a count or sequence from 0), always inside the limits of its SQL
    /// type. Null for a column that is not a whole number. </summary>
    public NumericRange? RangeFor(ColumnModel column)
    {
        if (!column.IsIntegerColumn)
            return null;
        var type = NumericClassifier.TypeRange(column.SqlType);
        (long min, long max) = column.NumericKind switch
        {
            NumericKind.Year => (MinYear, MaxYear),
            NumericKind.Month => (1L, 12L),
            NumericKind.DayOfMonth => (1L, 31L),
            NumericKind.Quarter => (1L, 4L),
            NumericKind.WeekNumber => (1L, 53L),
            NumericKind.Percentage => (0L, 100L),
            NumericKind.Count or NumericKind.Sequence => (0L, type.Max),
            _ => (type.Min, type.Max)
        };
        return new NumericRange(Math.Max(min, type.Min), Math.Min(max, type.Max));
    }

    /// <summary> The ISO 4217 code (USD, EUR, ...) a currency number box formats with. </summary>
    public string CurrencyCode => Explicit("CurrencyCode")?.ToUpperInvariant() ?? "USD";

    public const int DefaultEnumMaxRows = 25;

    /// <summary> A lookup table with more rows than this is not treated as an enum, however small its shape. </summary>
    /// <summary> A table whose name ends in one of these (and has an integer key and a text column) is treated as an enum
    /// even when its shape is not a bare lookup table -- the default suits names like LeaveType and MeterTypeCodes. </summary>
    public string[] EnumNameSuffixes =>
        Explicit("EnumNameSuffixes") is not null ? List("EnumNameSuffixes")! : ["Type", "Types", "Code", "Codes", "Status", "Kind"];

    private bool NameSaysEnum(string table) =>
        EnumNameSuffixes.Any(s => table.Length > s.Length && table.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    public int EnumMaxRows => int.TryParse(Explicit("EnumMaxRows"), out int rows) ? rows : DefaultEnumMaxRows;

    /// <summary> The tables listed in the project file as enums, or null when it does not list any (then the schema
    /// decides, see <see cref="IsEnumTable"/>). </summary>
    public string[]? EnumTables => Explicit("EnumTables") is null ? null
        : List("EnumTables") is { Length: 1 } one && one[0].Equals("none", StringComparison.OrdinalIgnoreCase) ? [] : List("EnumTables");

    /// <summary> Whether <paramref name="table"/> is one of this project's enum tables -- the ones with no entity,
    /// repository or API of their own. Null when no project is chosen (the caller keeps its own built-in list). A
    /// project's own EnumTables list wins; without one, a table is an enum when its schema looks like a lookup table
    /// (or its name ends in one of <see cref="EnumNameSuffixes"/> and it has an integer key and a text column) and it has
    /// at most <see cref="EnumMaxRows"/> rows. </summary>
    public bool? IsEnumTable(string table, LookupShape shape)
    {
        if (ProjectName is null)
            return null;
        if (EnumTables is { } listed)
            return listed.Contains(table, StringComparer.OrdinalIgnoreCase);
        return shape.RowCount <= EnumMaxRows && (shape.LooksLikeLookup || (shape.HasIntKeyAndText && NameSaysEnum(table)));
    }

    // Each of the four questions the templates ask about an enum table has its own optional list in the project file
    // (it wins for that question alone); otherwise they all share IsEnumTable's answer.
    private bool? Resolve(string key, string table, LookupShape shape) =>
        ProjectName is null ? null
        : Explicit(key) is not null ? List(key)!.Contains(table, StringComparer.OrdinalIgnoreCase)
        : IsEnumTable(table, shape);

    /// <summary> No API / TypeScript model or screen for this table. Null: no project, use the template's own list. </summary>
    public bool? NoApi(string table, LookupShape shape) => Resolve("NoApiTables", table, shape);
    /// <summary> No repository (and no API class) for this table. </summary>
    public bool? NoRepository(string table, LookupShape shape) => Resolve("NoRepositoryTables", table, shape);
    /// <summary> No navigation property to this table on the entity or TypeScript model that references it. </summary>
    public bool? NoNavigation(string table, LookupShape shape) => Resolve("NoNavigationTables", table, shape);
    /// <summary> A foreign key to this table is a plain number box rather than a drop-down of its rows. </summary>
    public bool? NoLookup(string table, LookupShape shape) => Resolve("NoLookupParents", table, shape);
}
