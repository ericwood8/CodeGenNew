namespace Shop.Api.Repositories;

/// <summary> The filter and the sort of the Product search. </summary>
public static class ProductSearchQuery
{
    public static IQueryable<Product> Filter(IQueryable<Product> query, string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            string nameTerm = name.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(nameTerm));
        }
        return query;
    }

    public static IQueryable<Product> Sort(IQueryable<Product> query, string? sortColumn, bool descending)
    {
        IOrderedQueryable<Product>? ordered = null;
        switch (sortColumn is { Length: <= 128 } ? sortColumn.Trim().ToLowerInvariant() : null)
        {
            case "productid":
                ordered = descending ? query.OrderByDescending(t => t.ProductId) : query.OrderBy(t => t.ProductId);
                break;
            case "name":
                ordered = descending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name);
                break;
            case "price":
                ordered = descending ? query.OrderByDescending(t => t.Price) : query.OrderBy(t => t.Price);
                break;
            case "rating":
                ordered = descending ? query.OrderByDescending(t => t.Rating) : query.OrderBy(t => t.Rating);
                break;
        }

        // the default order applies without a chosen sort and breaks ties after one
        return ordered is null
            ? query.OrderBy(t => t.Name).ThenBy(t => t.ProductId)
            : ordered.ThenBy(t => t.Name).ThenBy(t => t.ProductId);
    }
}
