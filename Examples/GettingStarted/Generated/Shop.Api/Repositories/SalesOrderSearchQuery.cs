namespace Shop.Api.Repositories;

/// <summary> The filter and the sort of the SalesOrder search. </summary>
public static class SalesOrderSearchQuery
{
    public static IQueryable<SalesOrder> Filter(IQueryable<SalesOrder> query)
    {
        return query;
    }

    public static IQueryable<SalesOrder> Sort(IQueryable<SalesOrder> query, string? sortColumn, bool descending)
    {
        IOrderedQueryable<SalesOrder>? ordered = null;
        switch (sortColumn is { Length: <= 128 } ? sortColumn.Trim().ToLowerInvariant() : null)
        {
            case "salesorderid":
                ordered = descending ? query.OrderByDescending(t => t.SalesOrderId) : query.OrderBy(t => t.SalesOrderId);
                break;
            case "customerid":
                ordered = descending ? query.OrderByDescending(t => t.Customer!.Name) : query.OrderBy(t => t.Customer!.Name);
                break;
            case "productid":
                ordered = descending ? query.OrderByDescending(t => t.Product!.Name) : query.OrderBy(t => t.Product!.Name);
                break;
            case "orderdate":
                ordered = descending ? query.OrderByDescending(t => t.OrderDate) : query.OrderBy(t => t.OrderDate);
                break;
            case "quantity":
                ordered = descending ? query.OrderByDescending(t => t.Quantity) : query.OrderBy(t => t.Quantity);
                break;
        }

        // the default order applies without a chosen sort and breaks ties after one
        return ordered is null
            ? query.OrderBy(t => t.SalesOrderId)
            : ordered.ThenBy(t => t.SalesOrderId);
    }
}
