using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Generation;

/// <summary> One template's place in a whole-project generation: what it runs for and where its files go. </summary>
public sealed record PlanStep(TemplateInfo Template, IReadOnlyList<string> Stacks, IReadOnlyList<string> TableNames)
{
    /// <summary> True for a template that runs once for the whole database (it gets the DatabaseModel). </summary>
    public bool IsDatabaseLevel => Template.Config.DatabaseOnly;
}

/// <summary> Works out which templates a project generates and for which tables, from the templates' own configs (<c>Stacks</c>, <c>PlanTables</c>, <c>InPlan</c>, <c>Dialects</c>) and the
/// project's settings (<c>Stacks</c>, <c>Screens</c>, <c>DetailMasterTables</c>, the enum rules, <c>PlanAlso</c>). It replaces the table lists every sample's Regenerate.sh wrote out by hand. </summary>
public static class ProjectPlan
{
    public static IReadOnlyList<string> KnownStacks { get; } = ["Api", "WinUI3", "React", "Angular", "Blazor", "Rust", "Python"];

    /// <summary> The tables a <see cref="PlanTableSet"/> stands for in <paramref name="database"/>, by name. </summary>
    public static List<TableModel> Tables(PlanTableSet set, DatabaseModel database, ProjectSettings project)
    {
        var screens = database.ScreenTables(project);
        return set switch
        {
            PlanTableSet.Entity => database.EntityTables.Where(t => project.NoRepository(t.TableName, t.LookupShape) != true).ToList(),
            PlanTableSet.Context => database.EntityTables.Where(t => project.NoRepository(t.TableName, t.LookupShape) != true).Concat(database.CompositeKeyTables).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList(),
            PlanTableSet.Api => database.ApiTables(project),
            PlanTableSet.Search => database.SearchApiTables(project),
            PlanTableSet.Screen => screens,
            PlanTableSet.ScreenDetailMaster => screens.Where(t => DatabaseModel.IsDetailMaster(t, project)).ToList(),
            PlanTableSet.ScreenForm => screens.Where(t => !DatabaseModel.IsDetailMaster(t, project)).ToList(),
            PlanTableSet.Junction => database.Tables.Where(t => t.IsJunctionTable).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList(),
            PlanTableSet.Enum => database.EnumTables(project),
            PlanTableSet.Audit => database.Tables.Where(t => t.IsAuditTable).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList(),
            PlanTableSet.Temporal => database.Tables.Where(t => project.TemporalTables.Contains(t.TableName, StringComparer.OrdinalIgnoreCase)).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => []
        };
    }

    /// <summary> The templates a run of <paramref name="stacks"/> includes, each with the stacks (of those asked for) it belongs to and its tables. A template the project's PlanAlso names
    /// is included although its config says <c>InPlan=false</c>; one for another database (<c>Dialects</c>) is left out. </summary>
    public static List<PlanStep> Build(IReadOnlyList<TemplateInfo> templates, DatabaseModel database, ProjectSettings project, IReadOnlyCollection<string> stacks)
    {
        string dialect = database.Dialect.ToString();
        var also = project.PlanAlso.Concat(project.ImpliedPlanTemplates).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var steps = new List<PlanStep>();
        foreach (var template in templates.Where(t => !t.IsSuperseded && !t.Config.NoDatabase).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var config = template.Config;
            var belongs = config.Stacks.Where(s => stacks.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (belongs.Count == 0)
                continue;
            if (!config.InPlan && !also.Contains(Normalize(template.Name)))
                continue;
            if (config.Dialects.Count > 0 && !config.Dialects.Contains(dialect, StringComparer.OrdinalIgnoreCase))
                continue;
            if (config.AccessMode is { } mode && mode != project.AccessModeFor(database.Dialect) && !also.Contains(Normalize(template.Name)))
                continue;

            var tableNames = config.DatabaseOnly ? [] : Tables(config.PlanTables, database, project).Select(t => t.TableName).ToList();
            steps.Add(new PlanStep(template, belongs, tableNames));
        }
        return steps;
    }

    /// <summary> "SP_Insert", "SP_Insert_v1" and "SP_Insert_v1.tt" are the same template. </summary>
    private static string Normalize(string name)
    {
        string stem = name.EndsWith(".tt", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
        return TemplateCatalog.ParseName(stem).BaseName;
    }
}
