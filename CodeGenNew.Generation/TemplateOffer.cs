using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Generation;

/// <summary> Whether the right-click menu offers a table template that depends on the project: a screen the Screens setting does not list, an OData page for a table the project does not serve as OData, a seed or a transfer
/// class for a table the project does not name are written as nothing by a whole-project run, so a single run would write an empty file or none. <see cref="TemplateInfo.AppliesTo"/> checks the table's own facts;
/// this checks what the project says. </summary>
public static class TemplateOffer
{
    private static readonly PlanTableSet[] ProjectDependent =
        [PlanTableSet.Screen, PlanTableSet.SingleRowScreen, PlanTableSet.ScreenDetailMaster, PlanTableSet.ScreenForm, PlanTableSet.ScreenDialog, PlanTableSet.Seed, PlanTableSet.Temporal, PlanTableSet.Dto];

    public static bool Applies(TemplateInfo template, TableModel table, DatabaseModel database, ProjectSettings project)
    {
        var config = template.Config;
        if (string.Equals(config.Variant, "OData", StringComparison.OrdinalIgnoreCase) && (!ODataPlan.HasPage(table, project) || table.IsNameActiveTable))
            return false;
        if (config.DatabaseOnly || !ProjectDependent.Contains(config.PlanTables))
            return true;
        return ProjectPlan.Tables(config.PlanTables, database, project, [.. config.Stacks]).Any(t => t.TableName == table.TableName);
    }

    /// <summary> The templates of <paramref name="templates"/> the menu offers on <paramref name="table"/>. </summary>
    public static IReadOnlyList<TemplateInfo> Filter(IEnumerable<TemplateInfo> templates, TableModel table, DatabaseModel database, ProjectSettings project) =>
        [.. templates.Where(t => Applies(t, table, database, project))];
}
