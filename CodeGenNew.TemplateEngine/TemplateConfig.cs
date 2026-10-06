using CodeGenNew.Core;

namespace CodeGenNew.TemplateEngine;

/// <summary> What a template's RequiredPrimaryKeyShape (below) needs a table's TableModel.PrimaryKeyShape /
/// TableSummary.PrimaryKeyShape to be -- three tiers, matching the three shapes actually needed across the
/// shipped templates (Docs/Reference.md section 3). </summary>
public enum PrimaryKeyRequirement
{
    /// <summary> Any single primary key column (int, uniqueidentifier, or a natural text/other key) -- just
    /// not composite. TS_Model.tt: its interface only needs ONE property to be "the key". </summary>
    SingleColumn,

    /// <summary> A single int or uniqueidentifier column. TS_Service.tt/TS_Component.tt/
    /// TS_DetailMasterComponent.tt: their routes take the id as {id:int} or {id:guid}. </summary>
    SingleIntOrGuid,

    /// <summary> A single int column only. API_Crud.tt and the WinUI3 CRUD-screen family: their repository
    /// calls (GenericRepo&lt;T&gt;'s GetById/UpdateAsync/DeleteAsync) all take a plain int. </summary>
    SingleInt
}

/// <summary> Parses a &lt;TemplateName&gt;.tt.config file (Docs/Reference.md section 3). Both restrictions
/// default to true when the file is missing entirely (conservative default for a hand-added .tt file). </summary>
public class TemplateConfig
{
    public bool RequiresPrimaryKey { get; init; } = true;
    public bool TableOnly { get; init; } = true;

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

    /// <summary> Optional, unset by default (no extra restriction beyond RequiresPrimaryKey). A template
    /// whose routes or repository calls need a specific key shape (not just "has a primary key") sets this
    /// so the menu/CLI refuse a table shaped wrong up front, instead of only finding out via the template's
    /// own Error() call once generation is attempted. Checked against TableSummary.PrimaryKeyShape /
    /// TableModel.PrimaryKeyShape. </summary>
    public PrimaryKeyRequirement? RequiredPrimaryKeyShape { get; init; }

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

    /// <summary> Defaults to false. A template that writes T-SQL (a stored procedure; or C# that EXECs one) sets this, so a table read from
    /// PostgreSQL is refused with a reason instead of getting code that cannot run there. SP_Search is not one of them: it writes PostgreSQL functions too. </summary>
    public bool SqlServerOnly { get; init; }

    /// <summary> Defaults to false. A template that writes ONE file for the whole database (the DbContext, the API registration) and so is
    /// given every table as <c>DatabaseModel</c> instead of one table. It needs no table name: the menu offers it on the database, and the
    /// CLI runs it without -t. </summary>
    public bool DatabaseOnly { get; init; }

    /// <summary> Defaults to false. A template that needs neither a table nor a database (the files every app of a kind needs, a directory listing): it is given only the
    /// <c>Project</c> settings. The CLI runs it without -S, -d or -t, and no table or database menu offers it. </summary>
    public bool NoDatabase { get; init; }

    /// <summary> The stacks whose generation includes this template (<c>Stacks=Api,WinUI3</c>): <c>Api</c>, <c>WinUI3</c>, <c>React</c> and <c>Angular</c>. Empty: the template is run by hand
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

    /// <summary> True when the given shape satisfies this config's RequiredPrimaryKeyShape (always true when
    /// RequiredPrimaryKeyShape is unset). </summary>
    public bool PrimaryKeyShapeSatisfies(PrimaryKeyShape shape) => RequiredPrimaryKeyShape switch
    {
        null => true,
        PrimaryKeyRequirement.SingleColumn => shape is PrimaryKeyShape.SingleInt or PrimaryKeyShape.SingleUniqueIdentifier or PrimaryKeyShape.SingleOther,
        PrimaryKeyRequirement.SingleIntOrGuid => shape is PrimaryKeyShape.SingleInt or PrimaryKeyShape.SingleUniqueIdentifier,
        PrimaryKeyRequirement.SingleInt => shape == PrimaryKeyShape.SingleInt,
        _ => true
    };

    /// <summary> Explains why this template does not apply to <paramref name="model"/> (checking the same
    /// restrictions as AppliesTo, but against a full TableModel and with the reason spelled out for a human
    /// instead of just a bool) -- or null when every restriction is satisfied. Shared by the CLI's own
    /// pre-check and the TreeView's right-click menu is the cheaper AppliesTo/TableSummary path instead,
    /// since building a full TableModel for every table up front is too expensive; this is for the one
    /// table a caller has already committed to generating against. Does not check TableOnly (TableModel
    /// doesn't carry an IsView flag in v1 -- generation is table-only already). </summary>
    public string? Refuse(TableModel model)
    {
        if (!SupportsDialect(model.Dialect))
            return $"is not written for {DialectInfo.For(model.Dialect).Name} (it is for {string.Join(", ", Dialects)}).";
        if (SqlServerOnly && model.Dialect != SqlDialect.SqlServer)
            return "writes T-SQL (a SQL Server stored procedure) and has no PostgreSQL version yet.";
        if (RequiresPrimaryKey && !model.HasPrimaryKey)
            return "requires a primary key, but the table doesn't have one.";
        if (RequiresJunctionTable && !model.IsJunctionTable)
            return "requires a many-to-many junction/bridge table, but the table isn't shaped like one.";
        if (RequiresChildTables && !model.HasAtLeastOneChildForeignKey)
            return "requires at least one other table with a foreign key pointing back at it, but none was found.";
        if (RequiresAuditTable && !model.IsAuditTable)
            return "requires a table with audit columns (one that records its creation, like CreateDate or CreateUser, and one that records a change, like ModifiedDate or ModifiedBy), but the table has none.";
        if (!PrimaryKeyShapeSatisfies(model.PrimaryKeyShape))
            return $"requires a {RequiredPrimaryKeyShape} primary key, but the table has a {model.PrimaryKeyShape} one.";
        if (RequiresNotNameActiveTable && model.IsNameActiveTable)
            return "assumes a plain GenericRepo-shaped backend, but the table has a Name and an IsActive column, " +
                   "so its repository is a NameActiveRepo and its real API is always hand-maintained.";
        return null;
    }

    public static TemplateConfig Load(string ttConfigPath)
    {
        if (!File.Exists(ttConfigPath))
            return new TemplateConfig();

        bool requiresPrimaryKey = true;
        bool tableOnly = true;
        bool requiresJunctionTable = false;
        bool requiresChildTables = false;
        bool requiresAuditTable = false;
        PrimaryKeyRequirement? requiredPrimaryKeyShape = null;
        bool requiresNotNameActiveTable = false;
        bool needsRowData = false;
        bool sqlServerOnly = false;
        bool needsReferencedDisplayColumns = false;
        bool databaseOnly = false;
        bool noDatabase = false;
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

            if (key.EqualsIgnoreCase("RequiresPrimaryKey"))
                requiresPrimaryKey = boolValue;
            else if (key.EqualsIgnoreCase("TableOnly"))
                tableOnly = boolValue;
            else if (key.EqualsIgnoreCase("RequiresJunctionTable"))
                requiresJunctionTable = boolValue;
            else if (key.EqualsIgnoreCase("RequiresChildTables"))
                requiresChildTables = boolValue;
            else if (key.EqualsIgnoreCase("RequiresAuditTable"))
                requiresAuditTable = boolValue;
            else if (key.EqualsIgnoreCase("RequiredPrimaryKeyShape"))
                requiredPrimaryKeyShape = Enum.TryParse<PrimaryKeyRequirement>(value, ignoreCase: true, out var parsed) ? parsed : null;
            else if (key.EqualsIgnoreCase("RequiresNotNameActiveTable"))
                requiresNotNameActiveTable = boolValue;
            else if (key.EqualsIgnoreCase("NeedsRowData"))
                needsRowData = boolValue;
            else if (key.EqualsIgnoreCase("SqlServerOnly"))
                sqlServerOnly = boolValue;
            else if (key.EqualsIgnoreCase("DatabaseOnly"))
                databaseOnly = boolValue;
            else if (key.EqualsIgnoreCase("NoDatabase"))
                noDatabase = boolValue;
            else if (key.EqualsIgnoreCase("Stacks"))
                stacks = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (key.EqualsIgnoreCase("PlanTables"))
                planTables = Enum.TryParse<PlanTableSet>(value, ignoreCase: true, out var set) ? set : PlanTableSet.Entity;
            else if (key.EqualsIgnoreCase("InPlan"))
                inPlan = boolValue;
            else if (key.EqualsIgnoreCase("Dialects"))
                dialects = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (key.EqualsIgnoreCase("AccessMode"))
                accessMode = Enum.TryParse<AccessMode>(value, ignoreCase: true, out var mode) ? mode : null;
            else if (key.EqualsIgnoreCase("OutputRoot"))
                outputRoot = value.Length > 0 ? value : "Stack";
            else if (key.EqualsIgnoreCase("OutputFolder"))
                outputFolders[""] = value;
            else if (key.StartsWith("OutputFolder.", StringComparison.OrdinalIgnoreCase))
                outputFolders[key["OutputFolder.".Length..]] = value;
            else if (key.EqualsIgnoreCase("EssentialsGroup"))
                essentialsGroup = value.Length > 0 ? value : null;
            else if (key.EqualsIgnoreCase("Description"))
                description = value.Length > 0 ? value : null;
            else if (key.EqualsIgnoreCase("Needs"))
                needs = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            else if (key.EqualsIgnoreCase("EssentialsDefault"))
                essentialsDefault = boolValue;
            else if (key.EqualsIgnoreCase("NeedsReferencedDisplayColumns"))
                needsReferencedDisplayColumns = boolValue;
            else if (key.EqualsIgnoreCase("OutputName"))
                outputName = value.Length > 0 ? value : null;
        }

        return new TemplateConfig
        {
            RequiresPrimaryKey = requiresPrimaryKey,
            TableOnly = tableOnly,
            RequiresJunctionTable = requiresJunctionTable,
            RequiresChildTables = requiresChildTables,
            RequiresAuditTable = requiresAuditTable,
            RequiredPrimaryKeyShape = requiredPrimaryKeyShape,
            RequiresNotNameActiveTable = requiresNotNameActiveTable,
            NeedsRowData = needsRowData,
            SqlServerOnly = sqlServerOnly,
            NeedsReferencedDisplayColumns = needsReferencedDisplayColumns,
            DatabaseOnly = databaseOnly,
            NoDatabase = noDatabase,
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
            OutputName = outputName
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
    /// <summary> The tables that also get a search routine and endpoint (a lookup table does not). </summary>
    Search,
    /// <summary> The tables with a screen (the project's Screens setting, else every table with a search). </summary>
    Screen,
    /// <summary> The screens whose dialog has no child grids. </summary>
    ScreenForm,
    /// <summary> The screens whose dialog also shows child grids. </summary>
    ScreenDetailMaster,
    /// <summary> The many-to-many junction tables. </summary>
    Junction,
    /// <summary> The enum (lookup) tables the project turns into C# enums. </summary>
    Enum,
    /// <summary> The tables with audit columns (<see cref="CodeGenNew.Core.TableModel.IsAuditTable"/>). </summary>
    Audit,
    /// <summary> The tables the project's <c>TemporalTables</c> setting names. </summary>
    Temporal
}
