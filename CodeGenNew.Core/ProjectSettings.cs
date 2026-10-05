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
        "Usings", "DetailMasterTables", "EnumTables", "EnumMaxRows", "EnumNameSuffixes", "HiddenParents", "ModelFileOverrides", "ChildGridTitles", "BaseEntity", "BaseNameActiveEntity", "NoLookupParents", "NoRepositoryTables", "NoApiTables", "NoNavigationTables", "NamingStyle", "Acronyms", "Screens", "NoCloneTables", "NonNegativeColumns", "ValidatorNamespace", "FakerNamespace", "ErdTables", "ApiDocs", "ApiHttp", "ApiFakers", "ProjectDocs", "ApiValidation", "ApiProduction", "Dashboard", "DashboardStrip", "DashboardMeasures", "NoDashboardTables", "AccessMode", "DtoNamespace", "FSharpNamespace", "ReplicationTargets", "KeySequenceTables", "KeySequenceTable", "BulkUpdateColumns", "BulkUpdateExpression", "ApiFolder", "ModelsFolder", "ServicesFolder", "ComponentsFolder", "PagesFolder", "DbSetNames", "AngularVersion", "IgnoredColumns", "ListingName", "ListingFolder", "ListingPattern",
        "Stacks", "PlanAlso", "OutputApi", "OutputWinUI3", "OutputReact", "OutputAngular", "OutputRust", "RustCrateName", "RustPort", "OutputSql", "AppNamespace", "DatabaseProvider", "DatabaseServer", "DatabaseName", "DatabaseUser", "ApiPort", "DevPort", "ProjectTitle",
        "BuildApi", "BuildWinUI3", "BuildReact", "BuildAngular", "TestApi", "TestWinUI3", "TestReact", "TestAngular"
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

    /// <summary> Tables that get no Clone button even though they could (<c>NoCloneTables=Customer,SalesInvoice</c>): copying a customer or an invoice is rarely what is wanted. </summary>
    public bool NoClone(string table) => Explicit("NoCloneTables") is { } text
        && text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(table, StringComparer.OrdinalIgnoreCase);

    private string[] Items(string key) => Explicit(key) is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    /// <summary> The databases a table's changes are copied to by SP_ReplicationTriggers (<c>ReplicationTargets=server1.Sales,server2.Sales</c>: linked server, then database). Empty when not set. </summary>
    public string[] ReplicationTargets => Items("ReplicationTargets");

    /// <summary> Tables whose primary key comes from the key-sequence table (SP_KeySequence) instead of an IDENTITY column (<c>KeySequenceTables=Customer,Item</c>): their insert routine asks the
    /// sequence for the next key. Empty when not set (every table keeps its own key rules). </summary>
    public bool UsesKeySequence(string table) => Items("KeySequenceTables").Contains(table, StringComparer.OrdinalIgnoreCase);

    public string[] KeySequenceTables => Items("KeySequenceTables");

    /// <summary> The table the key sequence is kept in (<c>KeySequenceTable=AutoInc</c>, the default). </summary>
    public string KeySequenceTable => Explicit("KeySequenceTable") ?? "AutoInc";

    /// <summary> Columns SP_BulkUpdate rewrites in every table that has one (<c>BulkUpdateColumns=Fnd,Acct</c>); empty when not set. </summary>
    public string[] BulkUpdateColumns => Items("BulkUpdateColumns");

    /// <summary> What each of those columns is set to, <c>{column}</c> standing for the column (<c>BulkUpdateExpression=UPPER({column})</c>, the default). </summary>
    public string BulkUpdateExpression => Explicit("BulkUpdateExpression") ?? "UPPER({column})";

    public string[] Acronyms => Explicit("Acronyms") is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    /// <summary> The namespace of the F# records FS_Entity writes (<c>FSharpNamespace=Shop.Domain</c>; derived from the project name as <c>Name.Domain</c>). </summary>
    public string? FSharpNamespace => Derived("FSharpNamespace", ".Domain");

    public string? ViewNamespace => Derived("ViewNamespace", ".App.Views");
    public string? ViewModelNamespace => Derived("ViewModelNamespace", ".App.ViewModels");
    public string? ContextName => Derived("ContextName", "Context");
    public string? ContextNamespace => Derived("ContextNamespace", ".App.Data");
    public string? ApiNamespace => Derived("ApiNamespace", ".ApiService.Apis");
    public string? EnumNamespace => Derived("EnumNamespace", ".App.Enums");
    public string? RepoNamespace => Derived("RepoNamespace", ".App.Repositories");
    /// <summary> The namespace of the data-transfer classes and mappers (CS_Dto, CS_Mapper, CS_DataContractDto, CS_TypedDataRow, CS_SerializationDtos): <c>DtoNamespace</c>, or <c>Name.App.Dtos</c>. </summary>
    public string? DtoNamespace => Derived("DtoNamespace", ".App.Dtos");
    /// <summary> The namespace of the FluentValidation validators CS_Validator writes: <c>ValidatorNamespace</c>, or <c>Name.App.Validators</c>. </summary>
    public string? ValidatorNamespace => Derived("ValidatorNamespace", ".App.Validators");

    /// <summary> The namespace of the Bogus fakers CS_Faker writes: <c>FakerNamespace</c>, or <c>Name.App.Fakers</c>. </summary>
    public string? FakerNamespace => Derived("FakerNamespace", ".App.Fakers");

    private bool Flag(string key) => Explicit(key) is { } text && (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary> <c>ApiDocs=true</c>: the plan also writes <c>openapi.yaml</c> (API_OpenApi) and the API serves it with a Swagger UI page at <c>/docs</c>. </summary>
    public bool ApiDocs => Flag("ApiDocs");

    /// <summary> <c>ApiHttp=true</c>: the plan also writes a <c>.http</c> request file per table (API_Http). </summary>
    public bool ApiHttp => Flag("ApiHttp");

    /// <summary> <c>ApiFakers=true</c>: the plan also writes a Bogus fake-data generator per table (CS_Faker), and the generated project references Bogus. </summary>
    public bool ApiFakers => Flag("ApiFakers");

    /// <summary> <c>ProjectDocs=true</c>: the plan also writes a data dictionary page per table and the ER diagram (MD_DataDictionary, MD_Erd). </summary>
    public bool ProjectDocs => Flag("ProjectDocs");

    /// <summary> <c>ApiProduction=true</c>: the API project also gets a production profile (ProductionProfile.cs, a Dockerfile) that uses only what ships in ASP.NET Core: Problem Details, health checks, rate limiting, response compression, security headers. </summary>
    public bool ApiProduction => Flag("ApiProduction");

    /// <summary> <c>ApiValidation=true</c>: the plan also writes a FluentValidation validator per table (CS_Validator), and the create and update endpoints run them (a 400 with the messages). </summary>
    public bool ApiValidation => Flag("ApiValidation");

    /// <summary> <c>Dashboard=true</c>: the plan also writes the dashboard (its queries, endpoint, page and a menu entry per front end, and a Markdown page and SQL script that list the widgets). </summary>
    public bool Dashboard => Flag("Dashboard");

    /// <summary> <c>DashboardStrip=true</c>: each screen of a table shows that table's two or three cards above its grid. Needs <see cref="Dashboard"/>. </summary>
    public bool DashboardStrip => Dashboard && Flag("DashboardStrip");

    /// <summary> <c>DashboardMeasures=SalesInvoice.TotalAmount:sum,SalesInvoice.TotalAmount:sum:InvoiceDate:month</c>: widgets the project names, which are always shown and replace the automatic widget of the same kind for that table. </summary>
    public string[] DashboardMeasures => Items("DashboardMeasures");

    /// <summary> <c>NoDashboardTables=AuditLog</c>: tables the dashboard leaves out. </summary>
    public string[] NoDashboardTables => Items("NoDashboardTables");

    /// <summary> <c>AccessMode=Routines</c> or <c>Ef</c>: how search, sort, paging, clone and the junction editors reach the database. Null when the project does not say; <see cref="AccessModes.For"/> decides then. </summary>
    public AccessMode? AccessModeSetting => Explicit("AccessMode")?.Trim().ToLowerInvariant() switch
    {
        "ef" => AccessMode.Ef,
        "routines" => AccessMode.Routines,
        _ => null
    };

    /// <summary> The access mode this project uses over the given database. </summary>
    public AccessMode AccessModeFor(SqlDialect dialect) => AccessModes.For(this, dialect);

    /// <summary> The templates the flags above add to a plan, as if <c>PlanAlso</c> named them. </summary>
    public IEnumerable<string> ImpliedPlanTemplates
    {
        get
        {
            if (ApiDocs) yield return "API_OpenApi";
            if (ApiHttp) yield return "API_Http";
            if (ApiFakers) yield return "CS_Faker";
            if (ProjectDocs) { yield return "MD_DataDictionary"; yield return "MD_Erd"; }
            if (ApiValidation) { yield return "CS_Validator"; yield return "RS_Validate"; }
            if (Dashboard)
            {
                foreach (string name in new[] { "CS_Dashboard", "API_Dashboard", "TSX_Dashboard", "TS_Dashboard", "WinUI3_DashboardPage", "SP_Dashboard", "MD_Dashboard" })
                    yield return name;
            }
        }
    }

    /// <summary> The tables MD_Erd draws (<c>ErdTables=Customer,SalesInvoice</c>); empty: every table. </summary>
    public string[] ErdTables => Explicit("ErdTables") is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    public string? EntityNamespace => Derived("EntityNamespace", ".App.Entities");

    public int MinYear => int.TryParse(Explicit("MinYear"), out int year) ? year : DefaultMinYear;
    public int MaxYear => int.TryParse(Explicit("MaxYear"), out int year) ? year : DefaultMaxYear;

    /// <summary> The folders the React and Angular templates write into and import from, relative to the front end's source folder (<c>src</c> for React, <c>srcpp</c> for Angular).
    /// The defaults are what both samples use. </summary>
    /// <summary> <c>DbSetNames=Plural</c> names the context's DbSet properties in the plural (<c>Customers</c>); the default is the table's own name (<c>Customer</c>), which is what the samples use. </summary>
    public bool PluralDbSets => string.Equals(Explicit("DbSetNames"), "Plural", StringComparison.OrdinalIgnoreCase);

    /// <summary> The major version of Angular the project runs (<c>AngularVersion=22</c>); null when the project does not say, which keeps the output every Angular version from 18 accepts
    /// (<c>standalone: true</c>, <c>*ngIf</c> / <c>*ngFor</c>, the default change detection spelled <c>Default</c>, the animation providers in the specs). </summary>
    public int? AngularVersion => int.TryParse(Explicit("AngularVersion"), out int version) && version > 0 ? version : null;

    /// <summary> Version 19 made every component standalone, so <c>standalone: true</c> is redundant from there on. </summary>
    public bool AngularStandaloneFlag => AngularVersion is null or < 19;

    /// <summary> Built-in <c>@if</c> / <c>@for</c> instead of <c>*ngIf</c> / <c>*ngFor</c> (stable from 18, the old directives deprecated from 20). </summary>
    public bool AngularControlFlow => AngularVersion is >= 18;

    /// <summary> The eager strategy is spelled <c>Eager</c> from 22 (<c>Default</c> is deprecated there) and <c>Default</c> before. </summary>
    public string AngularEagerStrategy => AngularVersion is >= 22 ? "Eager" : "Default";

    /// <summary> Angular Material's paginator needs the animation providers in a spec until the animations package was dropped (22). </summary>
    public bool AngularSpecAnimations => AngularVersion is null or < 22;

    /// <summary> A generated spec file without the animation providers when <see cref="AngularSpecAnimations"/> says they are not needed. </summary>
    public string AdaptAngularSpec(string spec) => AngularSpecAnimations ? spec
        : spec.Replace("import { provideNoopAnimations } from '@angular/platform-browser/animations';\n", "").Replace(", provideNoopAnimations()", "");

    /// <summary> The generated html with the built-in control flow when <see cref="AngularControlFlow"/> says so. </summary>
    public string AdaptAngularHtml(string html) => AngularControlFlow ? CodeGenNew.Core.AngularControlFlow.Convert(html) : html;

    /// <summary> Columns left out of every table (<c>IgnoredColumns=Tags,Place.Location</c>: a column name for every table, or <c>Table.Column</c>): a type CodeGenNew cannot map
    /// (a PostgreSQL array, geometry) is listed here so the rest of the table still generates. A primary key column is never left out. </summary>
    public string[] IgnoredColumns => Explicit("IgnoredColumns") is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    /// <summary> WinUI3_DirectoryListing: the class stem (<c>DocumentListPage</c>), the folder whose files are listed (environment variables are expanded when the app runs) and
    /// the file pattern. Null when not set; the template then uses <c>Document</c>, <c>%LocalAppData%\&lt;ProjectName&gt;\&lt;ListingName&gt;</c> and <c>*.*</c>. </summary>
    public string? ListingName => Explicit("ListingName");
    public string? ListingFolder => Explicit("ListingFolder");
    public string? ListingPattern => Explicit("ListingPattern");

    // ---- generating a whole project (codegen generate) and the essentials files

    /// <summary> The stacks a project generates (<c>Stacks=Api,React</c>): <c>Api</c>, <c>WinUI3</c>, <c>React</c>, <c>Angular</c>. Empty when not set (the command line then names them). </summary>
    public string[] Stacks => Explicit("Stacks") is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    /// <summary> Templates a plan runs although their config does not put them in it (<c>PlanAlso=SP_Insert,SP_Update</c>: the PostgreSQL / MySQL routines nothing calls by default). </summary>
    public string[] PlanAlso => Explicit("PlanAlso") is { } text ? text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];

    /// <summary> Where a stack's files go (<c>OutputApi=InvoiceSystem.Api</c>; relative to the output folder of the run). Null: the default, <c>&lt;ProjectName&gt;.Api</c>, <c>&lt;ProjectName&gt;.App</c>,
    /// <c>frontend</c> (React and Angular) and <c>sql</c>. </summary>
    public string OutputFolderOf(string root) => (root.ToLowerInvariant() switch
    {
        "api" => Explicit("OutputApi") ?? (ProjectName ?? "MyApp") + ".Api",
        "winui3" => Explicit("OutputWinUI3") ?? (ProjectName ?? "MyApp") + ".App",
        "react" => Explicit("OutputReact") ?? "frontend",
        "angular" => Explicit("OutputAngular") ?? "frontend",
        "rust" => Explicit("OutputRust") ?? (ProjectName ?? "MyApp") + ".Rust",
        "sql" => Explicit("OutputSql") ?? "sql",
        _ => root
    });

    /// <summary> The WinUI3 app's root namespace (App.xaml, MainWindow, GlobalUsings): <c>&lt;ProjectName&gt;.App</c> unless AppNamespace says otherwise. </summary>
    public string AppNamespace => Explicit("AppNamespace")
        ?? (ViewNamespace is { } views && views.EndsWith(".Views") ? views[..^".Views".Length] : (ProjectName ?? "MyApp") + ".App");

    /// <summary> The database the generated app talks to, for appsettings.json and the package reference: <c>SqlServer</c> (default), <c>PostgreSql</c> or <c>MySql</c>, the server (<c>localhost</c>),
    /// the database (the project name), the login (blank for SQL Server's Windows authentication). The password is never written: the context reads it from the environment. </summary>
    public SqlDialect DatabaseDialect => Explicit("DatabaseProvider")?.ToLowerInvariant() switch
    {
        "postgresql" or "postgres" or "pg" => SqlDialect.PostgreSql,
        "mysql" => SqlDialect.MySql,
        "sqlite" => SqlDialect.Sqlite,
        _ => SqlDialect.SqlServer
    };
    /// <summary> The crate (package) name of the Rust API: <c>RustCrateName</c>, or the project name in snake_case (<c>invoice_system</c>). </summary>
    public string RustCrateName => Explicit("RustCrateName") ?? RustNames.Crate(ProjectName ?? "my_app");

    /// <summary> The port the Rust API listens on: <c>RustPort</c>, else <see cref="ApiPort"/>, so the front ends' proxies need no change. </summary>
    public int RustPort => int.TryParse(Explicit("RustPort"), out int port) ? port : ApiPort;

    public string DatabaseServer => Explicit("DatabaseServer") ?? "localhost";
    public string DatabaseName => Explicit("DatabaseName") ?? ProjectName ?? "MyDatabase";
    public string? DatabaseUser => Explicit("DatabaseUser");

    /// <summary> The port the API listens on (default 5080), the dev server's (React 5173, Angular 4200) and the window title of a web app. </summary>
    public int ApiPort => int.TryParse(Explicit("ApiPort"), out int port) ? port : 5080;
    public int DevPort(string stack) => int.TryParse(Explicit("DevPort"), out int port) ? port : stack.Equals("Angular", StringComparison.OrdinalIgnoreCase) ? 4200 : 5173;
    public string ProjectTitle => Explicit("ProjectTitle") ?? System.Text.RegularExpressions.Regex.Replace(ProjectName ?? "My App", "(?<=[a-z0-9])(?=[A-Z])", " ");

    /// <summary> The command a stack's build or test step runs after a generate (<c>BuildReact=npm run build</c>, <c>TestApi=dotnet test</c>); null when the project says nothing (the stack's
    /// default applies), an empty string or <c>none</c> to switch the step off. <paramref name="step"/> is <c>build</c> or <c>test</c>. </summary>
    public string? StepCommand(string step, string stack)
    {
        string key = (step.Equals("test", StringComparison.OrdinalIgnoreCase) ? "Test" : "Build") + stack;
        return _values.FirstOrDefault(v => v.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    public string ApiFolder => Explicit("ApiFolder") ?? "api";
    public string ModelsFolder => Explicit("ModelsFolder") ?? "models";
    public string ServicesFolder => Explicit("ServicesFolder") ?? "services";
    public string ComponentsFolder => Explicit("ComponentsFolder") ?? "components";
    public string PagesFolder => Explicit("PagesFolder") ?? "pages";

    /// <summary> Money columns that can never be negative (<c>NonNegativeColumns=CreditLimit,Item.Cost</c>: a column name for every table, or <c>Table.Column</c> for one): their number box
    /// gets a minimum of 0. The schema cannot say so, because a check constraint is not read. </summary>
    public bool IsNonNegative(string table, string column) => Explicit("NonNegativeColumns") is { } text
        && text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(entry => entry.Equals(column, StringComparison.OrdinalIgnoreCase) || entry.Equals($"{table}.{column}", StringComparison.OrdinalIgnoreCase));

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
        // what the database says in a CHECK constraint narrows it further (CHECK (rating BETWEEN 1 AND 5))
        if (column.Check is { } check)
        {
            long checkedMin = Math.Max(min, check.IntegerMin ?? min), checkedMax = Math.Min(max, check.IntegerMax ?? max);
            if (checkedMin <= checkedMax)
                (min, max) = (checkedMin, checkedMax);
        }
        return new NumericRange(Math.Max(min, type.Min), Math.Min(max, type.Max));
    }

    /// <summary> The limits a money box (a currency column that is not a whole number) puts on its value: what the column's CHECK constraint says (<c>CHECK (credit_limit &gt;= 0)</c>), and a minimum of
    /// 0 for a column the project lists in <c>NonNegativeColumns</c> (for a database whose schema does not say so). Null for no limit. </summary>
    public (double? Min, double? Max) MoneyLimits(string table, ColumnModel column)
    {
        double? min = column.Check?.Min, max = column.Check?.Max;
        if (IsNonNegative(table, column.Name))
            min = min is null ? 0 : Math.Max(min.Value, 0);
        return (min, max);
    }

    private static string Number(double value) => value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary> <see cref="MoneyLimits"/> as XAML attributes (<c>Minimum="0" Maximum="100" </c>, with a trailing space), or empty. </summary>
    public string MoneyLimitAttributes(string table, ColumnModel column)
    {
        var (min, max) = MoneyLimits(table, column);
        return (min is { } lo ? $"Minimum=\"{Number(lo)}\" " : "") + (max is { } hi ? $"Maximum=\"{Number(hi)}\" " : "");
    }

    /// <summary> <see cref="MoneyLimits"/> as html input attributes (<c> min="0" max="100"</c>, with a leading space), or empty. </summary>
    public string MoneyLimitHtml(string table, ColumnModel column)
    {
        var (min, max) = MoneyLimits(table, column);
        return (min is { } lo ? $" min=\"{Number(lo)}\"" : "") + (max is { } hi ? $" max=\"{Number(hi)}\"" : "");
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
