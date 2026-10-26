namespace CodeGenNew.Core;

/// <summary> Which stacks show a table the project keeps one row of (<c>SingleRowTables</c>) as a form, and when it applies. A table served as OData keeps its OData pages. </summary>
public static class SingleRowPlan
{
    /// <summary> The stacks that have a single-record screen. </summary>
    public static IReadOnlyList<string> Stacks { get; } = ["WinUI3", "Uno", "React", "Angular", "Blazor"];

    /// <summary> A desktop stack still writes the table's dialogs (another table's child grid may open them); a web stack writes the one page and no master-detail page for the table. </summary>
    public static bool KeepsDialogs(string stack) => stack.Equals("WinUI3", StringComparison.OrdinalIgnoreCase) || stack.Equals("Uno", StringComparison.OrdinalIgnoreCase);

    /// <summary> Whether the table's screen on <paramref name="stack"/> is the master-detail one: a table with child grids, unless the stack shows it as the one-row form (a web page then has no child grids). </summary>
    public static bool IsDetailMasterScreen(TableModel table, ProjectSettings project, string stack) =>
        DatabaseModel.IsDetailMaster(table, project) && !(Applies(table, project, stack) && !KeepsDialogs(stack));

    /// <summary> A desktop stack writes the form page with a template of its own (a SingleRowScreen); a web stack makes its ordinary page template write the form. </summary>
    public static bool HasOwnTemplate(string stack) => KeepsDialogs(stack);

    /// <summary> The heading of a web page: a single-row page names its one record, a list its rows. </summary>
    public static string Heading(string title, bool oneRow) => oneRow ? title : title.Pluralize();

    /// <summary> A row of the table as a TypeScript object literal for a page's test: the key, then the starting value of every required property (what Add New starts with). </summary>
    public static string TypeScriptRow(FormModel form)
    {
        var key = form.KeyColumn!;
        string name = JsonNames.Camel(key.Name);
        string first = form.TextKey ? $"{name}: '{ApiSampleValues.KeyText(key)}'" : $"{name}: 1";
        return "{ " + string.Join(", ", new[] { first }.Concat(form.NewRowTypeScript.Where(a => !a.StartsWith(name + ":")))) + " }";
    }

    public static bool Has(string stack) => Stacks.Contains(stack, StringComparer.OrdinalIgnoreCase);

    /// <summary> Whether the table is shown as a form on <paramref name="stack"/>: the project lists it and the stack has the screen. A table served as OData keeps its OData pages, which are lists. </summary>
    public static bool Applies(TableModel table, ProjectSettings project, string stack) =>
        project.IsSingleRow(table.TableName) && Has(stack) && !(project.OData && ODataPlan.Serves(table, project));
}
