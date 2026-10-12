namespace Shop.Api.Repositories;

/// <summary> The filter and the sort of the Customer search. </summary>
public static class CustomerSearchQuery
{
    public static IQueryable<Customer> Filter(IQueryable<Customer> query, string? name = null, string? email = null, string? status = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            string nameTerm = name.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(nameTerm));
        }
        if (!string.IsNullOrWhiteSpace(email))
        {
            string emailTerm = email.Trim().ToLower();
            query = query.Where(t => t.Email != null && t.Email.ToLower().Contains(emailTerm));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            string statusTerm = status.Trim().ToLower();
            query = query.Where(t => t.Status.ToLower().Contains(statusTerm));
        }
        return query;
    }

    public static IQueryable<Customer> Sort(IQueryable<Customer> query, string? sortColumn, bool descending)
    {
        IOrderedQueryable<Customer>? ordered = null;
        switch (sortColumn is { Length: <= 128 } ? sortColumn.Trim().ToLowerInvariant() : null)
        {
            case "customerid":
                ordered = descending ? query.OrderByDescending(t => t.CustomerId) : query.OrderBy(t => t.CustomerId);
                break;
            case "name":
                ordered = descending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name);
                break;
            case "email":
                ordered = descending ? query.OrderByDescending(t => t.Email) : query.OrderBy(t => t.Email);
                break;
            case "creditlimit":
                ordered = descending ? query.OrderByDescending(t => t.CreditLimit) : query.OrderBy(t => t.CreditLimit);
                break;
            case "status":
                ordered = descending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status);
                break;
            case "istaxable":
                ordered = descending ? query.OrderByDescending(t => t.IsTaxable) : query.OrderBy(t => t.IsTaxable);
                break;
        }

        // the default order applies without a chosen sort and breaks ties after one
        return ordered is null
            ? query.OrderBy(t => t.Name).ThenBy(t => t.CustomerId)
            : ordered.ThenBy(t => t.Name).ThenBy(t => t.CustomerId);
    }
}
