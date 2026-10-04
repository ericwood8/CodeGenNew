namespace CodeGenNew.Core;

/// <summary> One text column a grid can filter by: a contains match; the parameter name is what the caller sends. </summary>
public sealed record SearchFilter(ColumnModel Column, string ParameterName);

/// <summary> One column a grid can be sorted by. <see cref="ParentPath"/> is the C# path to the parent's display property (<c>Customer.Name</c>) when the foreign key sorts by what the grid
/// shows, null when it sorts by the column itself. </summary>
public sealed record SearchSortOption(string Name, ColumnModel Column, string? ParentPath);

/// <summary> What a table's search does, stated once and independent of how it is carried out: the filters, the sort columns the caller may name, the default order and the paging limits. The routines
/// (SP_Search) and the LINQ query (CS_SearchQuery) follow it today; a Rust repository or a dashboard query reads the same plan. </summary>
public sealed class SearchPlan
{
    private SearchPlan(IReadOnlyList<SearchFilter> filters, IReadOnlyList<SearchSortOption> sorts, IReadOnlyList<ColumnModel> defaultOrder)
    {
        Filters = filters;
        Sorts = sorts;
        DefaultOrder = defaultOrder;
    }

    /// <summary> A text filter per searchable column: NULL or blank means no filter, anything else must be contained in the column, ignoring case. </summary>
    public IReadOnlyList<SearchFilter> Filters { get; }

    /// <summary> The names a caller may sort by; any other name gives the default order. </summary>
    public IReadOnlyList<SearchSortOption> Sorts { get; }

    /// <summary> The order that applies without a sort and follows a chosen sort as the tie-breaker: the best display column, then the key. </summary>
    public IReadOnlyList<ColumnModel> DefaultOrder { get; }

    /// <summary> The longest sort name that is looked at; a longer one is ignored. </summary>
    public int MaxSortNameLength => SearchSort.MaxNameLength;

    /// <summary> The plan for a table. <paramref name="navigations"/> are the entity's navigation properties (<see cref="EntityNavigations.Of"/>), so a foreign key sorts by its parent's display
    /// column only when the entity can reach it; without one it sorts by its own value. </summary>
    public static SearchPlan For(TableModel model, IReadOnlyList<EntityNavigation> navigations)
    {
        string LowerFirst(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
        var filters = model.SearchableColumns.Select(c => new SearchFilter(c, LowerFirst(c.Name))).ToList();

        var sorts = SearchSort.Entries(model).Select(entry =>
        {
            var navigation = entry.Parent is { } parent
                ? navigations.FirstOrDefault(n => n.Column == entry.Column && n.ForeignKey == parent)
                : null;
            string? path = navigation is not null ? navigation.Role + "." + entry.Parent!.ReferencedDisplayColumns[0] : null;
            return new SearchSortOption(entry.Name, entry.Column, path);
        }).ToList();

        var best = model.DisplayColumns.OrderBy(c => c.DisplayRank ?? int.MaxValue).ThenBy(c => c.OrdinalPosition).FirstOrDefault();
        var order = new List<ColumnModel>();
        if (best is not null)
            order.Add(best);
        order.AddRange(model.PrimaryKeyColumns.Where(pk => pk != best));
        return new SearchPlan(filters, sorts, order);
    }
}
