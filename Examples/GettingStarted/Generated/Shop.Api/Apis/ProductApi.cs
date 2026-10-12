namespace Shop.Api.Apis;

using System.Linq.Expressions;

/// <summary> The API of Product: the endpoints and their answers are CrudApi's (Apis/CrudApi.cs, the CrudApi essentials group); this class says which repository and key it uses. </summary>
public class ProductApi : CrudApi<Product, ProductRepo>
{
    protected override ProductRepo NewRepo(ShopContext context) => new(context);

    protected override Expression<Func<Product, int>> Key => c => c.ProductId;

    protected override bool CanLookup => true;

    protected override IQueryable<LookupItem> LookupQuery(IQueryable<Product> rows) =>
        rows.OrderBy(c => c.Name).Select(c => new LookupItem(c.ProductId, c.Name, null));

    protected override bool CanFindByName => true;

    protected override Task<List<Product>> FindByNameAsync(ProductRepo repo, string name) => repo.GetByName(name);

    protected override bool CanClone => true;

    protected override Task<int> CloneAsync(ProductRepo repo, int id) => repo.CloneAsync(id);
}
