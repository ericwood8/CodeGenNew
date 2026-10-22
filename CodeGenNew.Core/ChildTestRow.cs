namespace CodeGenNew.Core;

/// <summary> A believable row of a child table as JSON, for the generated tests of a master-detail page: the key, the foreign key to the parent row being edited, and a value for every other required column. </summary>
public static class ChildTestRow
{
    /// <summary> The row, belonging to the parent whose key is <paramref name="parentKeyJson"/> (a JSON literal: <c>1</c> or <c>"A1"</c>). </summary>
    public static string Json(ChildGrid grid, string parentKeyJson)
    {
        var members = new List<string>();
        foreach (var c in grid.Link.ReferencingTableColumns)
        {
            string name = JsonNames.Camel(c.Name);
            if (c.Name.Equals(grid.KeyName, StringComparison.OrdinalIgnoreCase))
                members.Add($"\"{name}\": " + (grid.KeyIsText ? "\"" + ApiSampleValues.KeyText(grid.KeyColumn!) + "\"" : c.IsGuidColumn ? "\"00000000-0000-0000-0000-000000000001\"" : "1"));
            else if (c.Name.Equals(grid.Link.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase))
                members.Add($"\"{name}\": {parentKeyJson}");
            else if (!c.IsNullable && !c.IsComputed)
                members.Add($"\"{name}\": " + (c.IsBooleanColumn ? "false" : c.IsGuidColumn ? "\"00000000-0000-0000-0000-000000000001\"" : TemplateHelpers.IsDate(c) ? "\"2026-01-01\"" : TemplateHelpers.IsNumber(c) ? "0" : "\"\""));
        }
        return "{ " + string.Join(", ", members) + " }";
    }
}
