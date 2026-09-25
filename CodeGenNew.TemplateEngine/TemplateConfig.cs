using CodeGenNew.Core;

namespace CodeGenNew.TemplateEngine;

/// <summary> What a template's RequiredPrimaryKeyShape (below) needs a table's TableModel.PrimaryKeyShape /
/// TableSummary.PrimaryKeyShape to be -- three tiers, matching the three shapes actually needed across the
/// shipped templates (Docs/specs.md section 5.3). </summary>
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

/// <summary> Parses a &lt;TemplateName&gt;.tt.config file (Docs/specs.md section 5.3). Both restrictions
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
    /// NOT expose a plain getAll() at all (confirmed against a real one, TimeEntryServer's
    /// DepartmentTeamApi.cs: its only "list" route is scoped to a parent, /department/{id}, not a bare
    /// GET api/departmentteams). A template that assumes the standard shape would generate a client that
    /// compiles but 404s. Checked against TableSummary.IsNameActiveTable / TableModel.IsNameActiveTable. </summary>
    public bool RequiresNotNameActiveTable { get; init; }

    /// <summary> Unlike the two restrictions above (which default to true), this defaults to false: only a
    /// template that generates from the table's actual data (e.g. SP_Load.tt) asks for its rows to be read. </summary>
    public bool NeedsRowData { get; init; }

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
        if (RequiresPrimaryKey && !model.HasPrimaryKey)
            return "requires a primary key, but the table doesn't have one.";
        if (RequiresJunctionTable && !model.IsJunctionTable)
            return "requires a many-to-many junction/bridge table, but the table isn't shaped like one.";
        if (RequiresChildTables && !model.HasAtLeastOneChildForeignKey)
            return "requires at least one other table with a foreign key pointing back at it, but none was found.";
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
        PrimaryKeyRequirement? requiredPrimaryKeyShape = null;
        bool requiresNotNameActiveTable = false;
        bool needsRowData = false;
        bool needsReferencedDisplayColumns = false;
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
            else if (key.EqualsIgnoreCase("RequiredPrimaryKeyShape"))
                requiredPrimaryKeyShape = Enum.TryParse<PrimaryKeyRequirement>(value, ignoreCase: true, out var parsed) ? parsed : null;
            else if (key.EqualsIgnoreCase("RequiresNotNameActiveTable"))
                requiresNotNameActiveTable = boolValue;
            else if (key.EqualsIgnoreCase("NeedsRowData"))
                needsRowData = boolValue;
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
            RequiredPrimaryKeyShape = requiredPrimaryKeyShape,
            RequiresNotNameActiveTable = requiresNotNameActiveTable,
            NeedsRowData = needsRowData,
            NeedsReferencedDisplayColumns = needsReferencedDisplayColumns,
            OutputName = outputName
        };
    }
}
