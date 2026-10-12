namespace Shop.Api.Repositories;

public partial class SalesOrderRepo : GenericRepo<SalesOrder>
{
    public SalesOrderRepo(ShopContext context) : base(context)
    {
    }

    // Copies a row into a new one and returns the new row's id.
    // createUser is who is cloning it, for a table that records its creator.
    public async Task<int> CloneAsync(int id, string? createUser = null)
    {
        var source = await GetByIdAsync(id) ?? throw new KeyNotFoundException($"No SalesOrder found to clone (id = {id}).");
        var copy = new SalesOrder
        {
            CustomerId = source.CustomerId,
            ProductId = source.ProductId,
            OrderDate = source.OrderDate,
            Quantity = source.Quantity
        };
        _dbSet.Add(copy);
        await _context.SaveChangesAsync();
        return copy.SalesOrderId;
    }

    public async Task<(List<SalesOrder> Items, int TotalCount)> SearchAsync(int pageNumber = 1, int pageSize = 100, string? sortColumn = null, bool sortDescending = false)
    {
        var query = SalesOrderSearchQuery.Filter(_dbSet);
        int totalCount = await query.CountAsync();
        int size = Math.Clamp(pageSize, 1, 1000);   // 0 or less is an error in the database, a huge page reads the whole table
        var items = await SalesOrderSearchQuery.Sort(query, sortColumn, sortDescending)
            .Skip((Math.Clamp(pageNumber, 1, int.MaxValue / size) - 1) * size).Take(size).ToListAsync();
        return (items, totalCount);
    }
}
