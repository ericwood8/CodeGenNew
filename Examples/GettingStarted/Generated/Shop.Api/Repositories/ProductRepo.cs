namespace Shop.Api.Repositories;

public partial class ProductRepo : GenericRepo<Product>
{
    public ProductRepo(ShopContext context) : base(context)
    {
    }

    public async Task<List<Product>> GetByName(string name)
    {
        return await _dbSet
            .Where(d => d.Name.StartsWith(name))
            .ToListAsync();
    }

    // Copies a row into a new one and returns the new row's id.
    // createUser is who is cloning it, for a table that records its creator.
    public async Task<int> CloneAsync(int id, string? createUser = null)
    {
        var source = await GetByIdAsync(id) ?? throw new KeyNotFoundException($"No Product found to clone (id = {id}).");
        var copy = new Product
        {
            Name = source.Name,
            Price = source.Price,
            Rating = source.Rating
        };
        _dbSet.Add(copy);
        await _context.SaveChangesAsync();
        return copy.ProductId;
    }

    public async Task<(List<Product> Items, int TotalCount)> SearchAsync(string? name = null, int pageNumber = 1, int pageSize = 100, string? sortColumn = null, bool sortDescending = false)
    {
        var query = ProductSearchQuery.Filter(_dbSet, name);
        int totalCount = await query.CountAsync();
        int size = Math.Clamp(pageSize, 1, 1000);   // 0 or less is an error in the database, a huge page reads the whole table
        var items = await ProductSearchQuery.Sort(query, sortColumn, sortDescending)
            .Skip((Math.Clamp(pageNumber, 1, int.MaxValue / size) - 1) * size).Take(size).ToListAsync();
        return (items, totalCount);
    }
}
