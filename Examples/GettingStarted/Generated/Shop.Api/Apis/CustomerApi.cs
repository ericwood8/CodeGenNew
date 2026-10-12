namespace Shop.Api.Apis;

using System.Linq.Expressions;

/// <summary> The API of Customer: the endpoints and their answers are CrudApi's (Apis/CrudApi.cs, the CrudApi essentials group); this class says which repository and key it uses. </summary>
public class CustomerApi : CrudApi<Customer, CustomerRepo>
{
    protected override CustomerRepo NewRepo(ShopContext context) => new(context);

    protected override Expression<Func<Customer, int>> Key => c => c.CustomerId;

    protected override bool CanLookup => true;

    protected override IQueryable<LookupItem> LookupQuery(IQueryable<Customer> rows) =>
        rows.OrderBy(c => c.Name).Select(c => new LookupItem(c.CustomerId, c.Name, null));

    protected override bool CanFindByName => true;

    protected override Task<List<Customer>> FindByNameAsync(CustomerRepo repo, string name) => repo.GetByName(name);

    protected override bool CanClone => true;

    protected override Task<int> CloneAsync(CustomerRepo repo, int id) => repo.CloneAsync(id);
}
