namespace Shop.Api.Apis;

using System.Linq.Expressions;

/// <summary> The API of SalesOrder: the endpoints and their answers are CrudApi's (Apis/CrudApi.cs, the CrudApi essentials group); this class says which repository and key it uses. </summary>
public class SalesOrderApi : CrudApi<SalesOrder, SalesOrderRepo>
{
    protected override SalesOrderRepo NewRepo(ShopContext context) => new(context);

    protected override Expression<Func<SalesOrder, int>> Key => c => c.SalesOrderId;

    protected override Expression<Func<SalesOrder, DateTime>> NewestFirst => c => c.OrderDate;

    protected override bool CanClone => true;

    protected override Task<int> CloneAsync(SalesOrderRepo repo, int id) => repo.CloneAsync(id);
}
