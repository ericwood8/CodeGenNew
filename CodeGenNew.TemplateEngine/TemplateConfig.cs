using CodeGenNew.Core;

namespace CodeGenNew.TemplateEngine;

/// <summary> Where a template runs: once for each table, once for the whole database, or with no database at all. </summary>
public enum TemplateScope
{
    /// <summary> Once for a table: the template receives <c>Model</c> (the default). </summary>
    Table,
    /// <summary> Once for the whole database: the template receives every table as <c>Database</c>. It needs no table name: the menu offers it on the database, and the CLI runs it without -t. </summary>
    Database,
    /// <summary> Neither a table nor a database (the files every app of a kind needs, a directory listing): the template receives only <c>Project</c>. The CLI runs it without -S, -d or -t, and no table or database menu offers it. </summary>
    None
}

/// <summary> The facts about one table that a template's restrictions are checked against. Built from a full <see cref="TableModel"/>, or from a table summary by the menu, so the two checks cannot disagree. </summary>
public readonly record struct TableFacts(bool HasPrimaryKey, PrimaryKeyShape PrimaryKeyShape, bool IsJunctionTable, bool HasChildForeignKeys, bool IsAuditTable, bool IsNameActiveTable, SqlDialect Dialect)
{
    public static TableFacts Of(TableModel model) =>
        new(model.HasPrimaryKey, model.PrimaryKeyShape, model.IsJunctionTable, model.HasAtLeastOneChildForeignKey, model.IsAuditTable, model.IsNameActiveTable, model.Dialect);
}

/// <summary> Parses a &lt;TemplateName&gt;.tt.config file (Docs/Reference.md section 3). A table template requires a primary key when the file is missing entirely (the conservative default for a hand-added .tt file). </summary>
public class TemplateConfig
{
    /// <summary> Where the template runs (<c>Scope=Table|Database|None</c>); Table when not set. </summary>
    public TemplateScope Scope { get; init; } = TemplateScope.Table;

    /// <summary> The table restrictions the template names in <c>Requires=PrimaryKey,SingleKey,NotNameActive,JunctionTable,ChildTables,AuditTable</c>. Without the key a table template requires a primary key and nothing else;
    /// <c>Requires=</c> (empty) requires nothing. Meaningless for a database or no-database template. </summary>
    public bool RequiresPrimaryKey { get; init; } = true;

    /// <summary> Defaults to false, unlike the two restrictions above: only a template built specifically
    /// for many-to-many junction/bridge tables (e.g. WinUI3_JunctionEditor) asks for this. Checked against
    /// TableSummary.IsJunctionTable / TableModel.IsJunctionTable. </summary>
    public bool RequiresJunctionTable { get; init; }

    /// <summary> Defaults to false: only a template that generates a child grid from
    /// TableModel.ChildForeignKeys (e.g. WinUI3_DetailMasterScreen) asks for this. Checked against
    /// TableSummary.HasChildForeignKeys / TableModel.HasAtLeastOneChildForeignKey. </summary>
    public bool RequiresChildTables { get; init; }

    /// <summary> Defaults to false: only a template for tables that track who created and who changed each row (SP_AuditTable) asks for this. Checked against
    /// TableSummary.IsAuditTable / TableModel.IsAuditTable. </summary>
    public bool RequiresAuditTable { get; init; }

    /// <summary> Defaults to false (a composite key is accepted). A template that reads its key's C#, TypeScript, Python or route form from <see cref="KeyType"/> (the screens, the clients, the CRUD API) sets
    /// this: the table must have ONE key column that is an int, a uniqueidentifier or text, the keys <see cref="KeyType"/> describes. The menu and the CLI then refuse a table shaped otherwise up front, instead
    /// of only finding out through the template's own Error() call. Checked against TableSummary.PrimaryKeyShape / TableModel.PrimaryKeyShape. Junction and child-grid templates leave it off:
    /// they go by their association columns, and a junction table's key may be composite. </summary>
    public bool RequiresSingleKey { get; init; }

    /// <summary> Defaults to false: only a template whose generated code assumes a plain GenericRepo-shaped
    /// backend (a bare getAll()/getById()/create()/update()/delete(), or the WinUI3 equivalent calling
    /// &lt;Table&gt;Repo directly) sets this. A "name/active" table's repository is a NameActiveRepo instead
    /// (API_Crud.tt already refuses it, for the same reason: it needs duplicate-name checks and trimming, so
    /// its real API is always hand-maintained) -- and a hand-maintained API for that shape commonly does
    /// NOT expose a plain getAll() at all (confirmed against a real one: its only "list" route is scoped to a parent, /department/{id}, not a bare
    /// GET api/departmentteams). A template that assumes the standard shape would generate a client that
    /// compiles but 404s. Checked against TableSummary.IsNameActiveTable / TableModel.IsNameActiveTable. </summary>
    public bool RequiresNotNameActiveTable { get; init; }

    /// <summary> Unlike the two restrictions above (which default to true), this defaults to false: only a
    /// template that generates from the table's actual data (e.g. SP_Load.tt) asks for its rows to be read. </summary>
    public bool NeedsRowData { get; init; }

    /// <summary> True for a template that writes ONE file for the whole database (the DbContext, the API registration): <c>Scope=Database</c>. </summary>
    public bool DatabaseOnly => Scope == TemplateScope.Database;

    /// <summary> True for a template that needs neither a table nor a database: <c>Scope=None</c>. </summary>
    public bool NoDatabase => Scope == TemplateScope.None;

    /// <summary> Keys the file used that this reader does not know (a typo, or a key from a later version): the restriction they were meant to state is NOT applied. Always empty for the shipped templates (a test checks). </summary>
    public IReadOnlyList<string> UnknownKeys { get; init; } = [];

    /// <summary> Keys of the earlier format (<c>RequiresPrimaryKey=</c> and the other true/false restrictions, <c>DatabaseOnly=</c>, <c>NoDatabase=</c>, <c>TableOnly=</c>, <c>SqlServerOnly=</c>) that the file still uses. They are read, so a template written
    /// for an earlier version keeps working; the shipped templates use none (a test checks). </summary>
    public IReadOnlyList<string> LegacyKeys { get; init; } = [];

    /// <summary> The stacks whose generation includes this template (<c>Stacks=Api,WinUI3</c>): <c>Api</c>, <c>WinUI3</c>, <c>React</c>, <c>Angular</c>, <c>Blazor</c>, <c>Uno</c>, <c>Rust</c> and <c>Python</c>. Empty: the template is run by hand
    /// only and is not part of "generate everything for a project". </summary>
    public IReadOnlyList<string> Stacks { get; init; } = [];

    /// <summary> Which tables a plan runs this template for (<c>PlanTables=Entity</c>): see <see cref="PlanTableSet"/>. Ignored for a database-level or no-database template, which runs once. </summary>
    public PlanTableSet PlanTables { get; init; } = PlanTableSet.Entity;

    /// <summary> Where the template's files go: <c>Stack</c> (the stack's own project folder, the default) or <c>Sql</c> (the project's SQL folder). </summary>
    public string OutputRoot { get; init; } = "Stack";

    /// <summary> Defaults to true. A template with <c>InPlan=false</c> is run in a whole-project generate only when the project's <c>PlanAlso</c> setting names it (the PostgreSQL / MySQL
    /// insert, update, save, delete and lookup routines, which nothing calls by default). </summary>
    public bool InPlan { get; init; } = true;

    /// <summary> The databases this template is for (<c>Dialects=PostgreSql</c>); a plan skips it, silently, for another one. Empty: every database. </summary>
    public IReadOnlyList<string> Dialects { get; init; } = [];

    /// <summary> The access mode this template belongs to (<c>AccessMode=Routines</c>: it writes routines the generated code calls; <c>AccessMode=Ef</c>: it writes the LINQ replacement for them). A plan skips it when the project's
    /// mode (<see cref="AccessModes.For"/>) is the other one. Null: it belongs to both. </summary>
    public AccessMode? AccessMode { get; init; }

    /// <summary> True when the template is for the given database (its <c>Dialects</c> list is empty or names it). </summary>
    public bool SupportsDialect(SqlDialect dialect) => Dialects.Count == 0 || Dialects.Contains(dialect.ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary> The folder under the root the template's own relative paths land in (<c>OutputFolder=Entities</c>, or per stack: <c>OutputFolder.React=src</c>, <c>OutputFolder.Angular=src/app</c>). </summary>
    public string OutputFolderFor(string stack) =>
        _outputFolders.TryGetValue(stack, out string? folder) ? folder : _outputFolders.GetValueOrDefault("", "");

    internal Dictionary<string, string> _outputFolders { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary> A no-database template that is one of the files every app of a stack needs (an "essentials" group): its name in the Essentials menu (<c>EssentialsGroup=MainWindow</c>), one line
    /// saying what it writes (<c>Description=...</c>) and whether it is ticked the first time (<c>EssentialsDefault=false</c> for an optional one). </summary>
    public string? EssentialsGroup { get; init; }
    public string? Description { get; init; }
    public bool EssentialsDefault { get; init; } = true;

    /// <summary> Files (relative to the stack's project folder, <c>{Context}</c> standing for the context's name) that the files of an essentials group assume another run has written
    /// (<c>Needs=MainWindow.Screens.cs,Data/{Context}.cs</c>); a run warns when one is missing. </summary>
    public IReadOnlyList<string> Needs { get; init; } = [];

    /// <summary> Defaults to false. A template that shows the display columns of foreign-keyed tables (SP_Lookup) asks
    /// for them to be looked up (ForeignKeyModel.ReferencedDisplayColumns). </summary>
    public bool NeedsReferencedDisplayColumns { get; init; }

    /// <summary> Optional. When set, the generated file is named by this pattern instead of "<Table>_<Suffix>.<ext>"; the token
    /// {Table} is replaced by the table name (e.g. "{Table}Api.cs" gives E_DonateLeaveApi.cs). </summary>
    public string? OutputName { get; init; }

    /// <summary> Optional. A template whose file is only a stub and whose config says <c>Base=TS_Component_v1.tt</c> runs that template's text (a file in the same folder) instead of its own, with the template parameter
    /// <c>Variant</c> set to <see cref="Variant"/>; the base template writes its second form (another name, another service) when it sees a variant. The stub's own config says where the output goes. </summary>
    public string? Base { get; init; }

    /// <summary> Optional, with <see cref="Base"/>: the value of the base template's <c>Variant</c> parameter (for example <c>OData</c>). Null for an ordinary template. </summary>
    public string? Variant { get; init; }

    /// <summary> True when the given shape satisfies this config's <see cref="RequiresSingleKey"/> (always true when it is off). </summary>
    public bool PrimaryKeyShapeSatisfies(PrimaryKeyShape shape) =>
        !RequiresSingleKey || shape is PrimaryKeyShape.SingleInt or PrimaryKeyShape.SingleUniqueIdentifier or PrimaryKeyShape.SingleText;

    /// <summary> Explains why this template does not apply to <paramref name="model"/> (the reason spelled out for a person), or null when every restriction is satisfied. For the one table a caller has already
    /// committed to generating against; a menu uses <see cref="Check"/> on the cheaper table summary. </summary>
    public string? Refuse(TableModel model) => Check(TableFacts.Of(model));

    /// <summary> The one place the restrictions are checked: why the template does not apply to a table with these facts, or null when it does. </summary>
    public string? Check(TableFacts table)
    {
        if (!SupportsDialect(table.Dialect))
            return $"is not written for {DialectInfo.For(table.Dialect).Name} (it is for {string.Join(", ", Dialects)}).";
        if (RequiresPrimaryKey && !table.HasPrimaryKey)
            return "requires a primary key, but the table doesn't have one.";
        if (RequiresJunctionTable && !table.IsJunctionTable)
            return "requires a many-to-many junction/bridge table, but the table isn't shaped like one.";
        if (RequiresChildTables && !table.HasChildForeignKeys)
            return "requires at least one other table with a foreign key pointing back at it, but none was found.";
        if (RequiresAuditTable && !table.IsAuditTable)
            return "requires a table with audit columns (one that records its creation, like CreateDate or CreateUser, and one that records a change, like ModifiedDate or ModifiedBy), but the table has none.";
        if (!PrimaryKeyShapeSatisfies(table.PrimaryKeyShape))
            return $"requires a single int, uniqueidentifier or text primary key column, but the table has a {table.PrimaryKeyShape} one.";
        if (RequiresNotNameActiveTable && table.IsNameActiveTable)
            return "assumes a plain GenericRepo-shaped backend, but the table has a Name and an IsActive column, " +
                   "so its repository is a NameActiveRepo and its real API is always hand-maintained.";
        return null;
    }

    private static readonly string[] RequiresNames = ["PrimaryKey", "SingleKey", "NotNameActive", "JunctionTable", "ChildTables", "AuditTable"];

    // Keys of the earlier format. TableOnly never had an effect and SqlServerOnly is Dialects=SqlServer, so both are read and dropped / translated.
    private static readonly string[] LegacyKeyNames =
        ["RequiresPrimaryKey", "RequiresSingleKey", "RequiresNotNameActiveTable", "RequiresJunctionTable", "RequiresChildTables", "RequiresAuditTable", "DatabaseOnly", "NoDatabase", "TableOnly", "SqlServerOnly"];

    private static readonly string[] KnownKeyNames =
        ["Scope", "Requires", "NeedsRowData", "Stacks", "PlanTables", "InPlan", "Dialects", "AccessMode", "OutputRoot", "OutputFolder", "EssentialsGroup", "Description", "Needs", "EssentialsDefault",
         "NeedsReferencedDisplayColumns", "OutputName", "Base", "Variant"];

    public static TemplateConfig Load(string ttConfigPath)
    {
        if (!File.Exists(ttConfigPath))
            return new TemplateConfig();

        var scope = TemplateScope.Table;
        bool requiresPrimaryKey = true;
        bool requiresJunctionTable = false;
        bool requiresChildTables = false;
        bool requiresAuditTable = false;
        bool requiresSingleKey = false;
        bool requiresNotNameActiveTable = false;
        bool needsRowData = false;
        bool sqlServerOnly = false;
        bool needsReferencedDisplayColumns = false;
        var stacks = new List<string>();
        var planTables = PlanTableSet.Entity;
        string outputRoot = "Stack";
        bool inPlan = true;
        var dialects = new List<string>();
        AccessMode? accessMode = null;
        var outputFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? essentialsGroup = null, description = null;
        bool essentialsDefault = true;
        var needs = new List<string>();
        string? outputName = null;
        string? baseTemplate = null, variant = null;
        var unknown = new List<string>();
        var legacy = new List<string>();

        foreach (string rawLine in File.ReadAllLines(ttConfigPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex < 0)
                continue;

            string key = line[..equalsIndex].Trim();
            string value = line[(equalsIndex + 1)..].Trim();
            bool boolValue = value.EqualsIgnoreCase("true");
            var list = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

            if (LegacyKeyNames.Contains(key, StringComparer.OrdinalIgnoreCase))
                legacy.Add(key);
            else if (!KnownKeyNames.Contains(key, StringComparer.OrdinalIgnoreCase) && !key.StartsWithIgnoreCase("OutputFolder."))
                unknown.Add(key);

            if (key.EqualsIgnoreCase("Scope"))
            {
                if (Enum.TryParse<TemplateScope>(value, ignoreCase: true, out var parsedScope))
                    scope = parsedScope;
                else
                    unknown.Add("Scope=" + value);
            }
            else if (key.EqualsIgnoreCase("Requires"))
            {
                unknown.AddRange(list.Where(item => !RequiresNames.Contains(item, StringComparer.OrdinalIgnoreCase)).Select(item => "Requires=" + item));
                bool Has(string name) => list.Contains(name, StringComparer.OrdinalIgnoreCase);
                requiresPrimaryKey = Has("PrimaryKey");
                requiresSingleKey = Has("SingleKey");
                requiresNotNameActiveTable = Has("NotNameActive");
                requiresJunctionTable = Has("JunctionTable");
                requiresChildTables = Has("ChildTables");
                requiresAuditTable = Has("AuditTable");
            }
            else if (key.EqualsIgnoreCase("RequiresPrimaryKey"))
                requiresPrimaryKey = boolValue;
            else if (key.EqualsIgnoreCase("RequiresJunctionTable"))
                requiresJunctionTable = boolValue;
            else if (key.EqualsIgnoreCase("RequiresChildTables"))
                requiresChildTables = boolValue;
            else if (key.EqualsIgnoreCase("RequiresAuditTable"))
                requiresAuditTable = boolValue;
            else if (key.EqualsIgnoreCase("RequiresSingleKey"))
                requiresSingleKey = boolValue;
            else if (key.EqualsIgnoreCase("RequiresNotNameActiveTable"))
                requiresNotNameActiveTable = boolValue;
            else if (key.EqualsIgnoreCase("NeedsRowData"))
                needsRowData = boolValue;
            else if (key.EqualsIgnoreCase("SqlServerOnly"))
                sqlServerOnly = boolValue;
            else if (key.EqualsIgnoreCase("DatabaseOnly"))
            {
                if (boolValue)
                    scope = TemplateScope.Database;
            }
            else if (key.EqualsIgnoreCase("NoDatabase"))
            {
                if (boolValue)
                    scope = TemplateScope.None;
            }
            else if (key.EqualsIgnoreCase("Stacks"))
                stacks = list;
            else if (key.EqualsIgnoreCase("PlanTables"))
                planTables = Enum.TryParse<PlanTableSet>(value, ignoreCase: true, out var set) ? set : PlanTableSet.Entity;
            else if (key.EqualsIgnoreCase("InPlan"))
                inPlan = boolValue;
            else if (key.EqualsIgnoreCase("Dialects"))
                dialects = list;
            else if (key.EqualsIgnoreCase("AccessMode"))
                accessMode = Enum.TryParse<AccessMode>(value, ignoreCase: true, out var mode) ? mode : null;
            else if (key.EqualsIgnoreCase("OutputRoot"))
                outputRoot = value.Length > 0 ? value : "Stack";
            else if (key.EqualsIgnoreCase("OutputFolder"))
                outputFolders[""] = value;
            else if (key.StartsWithIgnoreCase("OutputFolder."))
                outputFolders[key["OutputFolder.".Length..]] = value;
            else if (key.EqualsIgnoreCase("EssentialsGroup"))
                essentialsGroup = value.Length > 0 ? value : null;
            else if (key.EqualsIgnoreCase("Description"))
                description = value.Length > 0 ? value : null;
            else if (key.EqualsIgnoreCase("Needs"))
                needs = list;
            else if (key.EqualsIgnoreCase("EssentialsDefault"))
                essentialsDefault = boolValue;
            else if (key.EqualsIgnoreCase("NeedsReferencedDisplayColumns"))
                needsReferencedDisplayColumns = boolValue;
            else if (key.EqualsIgnoreCase("OutputName"))
                outputName = value.Length > 0 ? value : null;
            else if (key.EqualsIgnoreCase("Base"))
                baseTemplate = value.Length > 0 ? value : null;
            else if (key.EqualsIgnoreCase("Variant"))
                variant = value.Length > 0 ? value : null;
        }

        // SqlServerOnly=true was the earlier way to write Dialects=SqlServer
        if (sqlServerOnly && dialects.Count == 0)
            dialects = ["SqlServer"];

        return new TemplateConfig
        {
            Scope = scope,
            RequiresPrimaryKey = requiresPrimaryKey,
            RequiresJunctionTable = requiresJunctionTable,
            RequiresChildTables = requiresChildTables,
            RequiresAuditTable = requiresAuditTable,
            RequiresSingleKey = requiresSingleKey,
            RequiresNotNameActiveTable = requiresNotNameActiveTable,
            NeedsRowData = needsRowData,
            NeedsReferencedDisplayColumns = needsReferencedDisplayColumns,
            Stacks = stacks,
            PlanTables = planTables,
            OutputRoot = outputRoot,
            InPlan = inPlan,
            Dialects = dialects,
            AccessMode = accessMode,
            _outputFolders = outputFolders,
            EssentialsGroup = essentialsGroup,
            Description = description,
            EssentialsDefault = essentialsDefault,
            Needs = needs,
            OutputName = outputName,
            Base = baseTemplate,
            Variant = variant,
            UnknownKeys = unknown,
            LegacyKeys = legacy
        };
    }
}

/// <summary> Which tables a template runs for when a whole project is generated (the template's <c>PlanTables</c> config key). </summary>
public enum PlanTableSet
{
    /// <summary> Every table that gets an entity and a repository: a single-column key and not an enum table. </summary>
    Entity,
    /// <summary> The <see cref="Entity"/> tables and the tables with a composite key (the junction tables): every class the DbContext has a DbSet for, except an enum table. </summary>
    Context,
    /// <summary> The tables that get a CRUD API (<see cref="CodeGenNew.Core.DatabaseModel.ApiTables"/>). </summary>
    Api,
    /// <summary> The tables with a plain CRUD API of any key type (<see cref="CodeGenNew.Core.DatabaseModel.CrudApiTables"/>): the <see cref="Api"/> tables and the guid and text key tables. </summary>
    CrudApi,
    /// <summary> The tables the ASP.NET API serves: the <see cref="Api"/> tables and the name/active tables (<see cref="CodeGenNew.Core.DatabaseModel.AspNetApiTables"/>). </summary>
    AspNetApi,
    /// <summary> The tables that also get a search routine and endpoint (a lookup table does not). </summary>
    Search,
    /// <summary> The <see cref="Search"/> tables and the uniqueidentifier and text key tables that have a search (<see cref="CodeGenNew.Core.DatabaseModel.AspNetSearchApiTables"/>). </summary>
    AspNetSearch,
    /// <summary> The <see cref="Entity"/> tables the project gives an API to (not the ones <c>NoApiTables</c> names): what a front end's model, service and api module are written for, since an app never receives a row of the others. </summary>
    Client,
    /// <summary> The tables with a screen (the project's Screens setting, else every table with a search). </summary>
    Screen,
    /// <summary> The screens of the tables the project's SingleRowTables names, for a stack that has a single-record screen: a form for the one row. </summary>
    SingleRowScreen,
    /// <summary> The screens whose dialog has no child grids. </summary>
    ScreenForm,
    /// <summary> The screens whose dialog also shows child grids. </summary>
    ScreenDetailMaster,
    /// <summary> Every screen's dialog, with or without child grids (the desktop stacks keep a dialog even for a one-row table). </summary>
    ScreenDialog,
    /// <summary> The tables the project's <c>SeedTables</c> names (<see cref="CodeGenNew.Core.DatabaseModel.SeedTables"/>). </summary>
    Seed,
    /// <summary> The tables whose API takes an input type and answers a transfer class (<see cref="CodeGenNew.Core.DatabaseModel.DtoTables"/>). </summary>
    Dto,
    /// <summary> The many-to-many junction tables. </summary>
    Junction,
    /// <summary> The enum (lookup) tables the project turns into C# enums. </summary>
    Enum,
    /// <summary> The tables with audit columns (<see cref="CodeGenNew.Core.TableModel.IsAuditTable"/>). </summary>
    Audit,
    /// <summary> The tables the project's <c>TemporalTables</c> setting names. </summary>
    Temporal
}
