namespace CodeGenNew.Core;

/// <summary> One header a grid can sort by and the OData path that sorts by it: the column itself (<c>accountNumber</c>) or, for a foreign key, what the grid shows of the parent (<c>customerStatus/description</c>). </summary>
public sealed record ODataSort(string Name, string Path);

/// <summary> One search box of a screen and the property it filters (a contains match, ignoring case). </summary>
public sealed record ODataFilter(string Key, string Property);

/// <summary> What an OData service of a table needs to know that the entity set does not say: the key, which boxes filter which properties, which header sorts by which path, the order without a sort and the
/// properties a drop-down reads. Every name is the JSON name the REST routes use. The searches and sorts are the table's <see cref="SearchPlan"/>, so the REST screen and the OData screen offer the same ones. </summary>
public sealed class ODataScreenPlan
{
    private ODataScreenPlan() { }

    public required string Set { get; init; }
    public required string KeyProperty { get; init; }
    public required IReadOnlyList<ODataFilter> Filters { get; init; }
    public required IReadOnlyList<ODataSort> Sorts { get; init; }
    public required IReadOnlyList<string> DefaultOrder { get; init; }

    /// <summary> The property a drop-down shows, or null when the table has no display column (it has no lookup). </summary>
    public string? LookupName { get; init; }

    /// <summary> The flag that tells an inactive row, or null. </summary>
    public string? LookupActive { get; init; }

    /// <summary> The plan of <paramref name="table"/>. A foreign key sorts by its parent's display column only when the entity can reach the parent and the parent is served as OData
    /// (<paramref name="servedTables"/>, by table name); otherwise it sorts by its own value. The parent's display column is the one its drop-down shows (the first of its <see cref="TableModel.DisplayColumns"/>),
    /// read from the served tables, so it does not need the foreign key to carry it (a database-level template's model does not). </summary>
    public static ODataScreenPlan For(TableModel table, ProjectSettings project, IReadOnlyDictionary<string, TableModel> servedTables)
    {
        var navigations = EntityNavigations.Of(table, fk => project.NoNavigationFor(fk.ReferencedTable, fk.ReferencedLookupShape));
        var search = SearchPlan.For(table, navigations);

        var sorts = SearchSort.Entries(table).Select(entry =>
        {
            string own = JsonNames.Camel(entry.Column.Name);
            var navigation = navigations.FirstOrDefault(n => n.Column == entry.Column);
            if (navigation is null
                || navigation.ForeignKey.ReferencedColumns.Count != 1
                || !servedTables.TryGetValue(navigation.ForeignKey.ReferencedTable, out var parent)
                || parent.DisplayColumns.FirstOrDefault() is not { } shown)
                return new ODataSort(entry.Name, own);
            return new ODataSort(entry.Name, JsonNames.Camel(navigation.Role) + "/" + JsonNames.Camel(shown.Name));
        }).ToList();

        var display = table.DisplayColumns.FirstOrDefault();
        var active = table.Columns.FirstOrDefault(c => c.IsBooleanColumn && !c.IsNullable && c.Name == "IsActive");
        return new ODataScreenPlan
        {
            Set = ODataNames.SetName(table.TableName),
            KeyProperty = JsonNames.Camel(table.PrimaryKeyColumns[0].Name),
            Filters = search.Filters.Select(f => new ODataFilter(f.ParameterName, JsonNames.Camel(f.Column.Name))).ToList(),
            Sorts = sorts,
            DefaultOrder = search.DefaultOrder.Select(c => JsonNames.Camel(c.Name)).ToList(),
            LookupName = display is null ? null : JsonNames.Camel(display.Name),
            LookupActive = active is null ? null : JsonNames.Camel(active.Name)
        };
    }
}
