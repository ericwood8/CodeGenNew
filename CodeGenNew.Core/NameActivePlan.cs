namespace CodeGenNew.Core;

/// <summary> The sentences of a table with a Name and an IsActive column, the same in every stack that serves it (the ASP.NET API's <c>NameActiveCrudApi</c> is the first): a name is trimmed and may not be empty, and a second
/// active row with the same name is refused with a 409 problem whose detail the screens show as it is. </summary>
public static class NameActiveWords
{
    public const string RequiredTitle = "Name is required";
    public const string RequiredDetail = "The name cannot be empty.";
    public const string DuplicateTitle = "Duplicate name";

    /// <summary> The detail of the duplicate-name problem: <c>An active row named 'x' already exists.</c> </summary>
    public static string DuplicateDetail(string name) => $"An active row named '{name.Trim()}' already exists.";

    /// <summary> The same sentence as a template for the generated code: <c>{0}</c> stands for the trimmed name. </summary>
    public const string DuplicateDetailFormat = "An active row named '{0}' already exists.";
}

/// <summary> A table with a Name and an IsActive column that the Python and Rust APIs serve: the list by name, the active list (<c>GET &lt;route&gt;/active</c>), the lookup, one row, find by name (<c>GET &lt;route&gt;/{name}</c>,
/// the active rows whose name contains the text), create, update and delete, with the duplicate-name rule of <see cref="NameActiveWords"/>. It has no search, no paging and no clone, like the ASP.NET API. </summary>
public sealed record NameActivePlan(TableModel Table, ColumnModel Name, ColumnModel Active)
{
    public static NameActivePlan? For(TableModel table, ProjectSettings project)
    {
        if (!table.HasNameActiveApi(project) || TableModel.FindRequiredName(table.Columns) is not { } name || TableModel.FindIsActive(table.Columns) is not { } active)
            return null;
        return new NameActivePlan(table, name, active);
    }
}
