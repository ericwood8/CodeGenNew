using System.Linq.Expressions;
using Shop.Api.Data;

namespace Shop.Api.Repositories;

/// <summary> Repository for any table: the shared base every generated <Table>Repo (CS_Repo) derives from. DeleteAsync attempts the delete and catches the foreign-key violation EF Core
/// reports as a DbUpdateException, returning the "in use" result the generated screens expect (0 = deleted, -1 = no longer exists, 1 = in use). The key's type is <typeparamref name="TKey"/>
/// (int, long, Guid or string: see <c>GenericRepo&lt;T&gt;</c> for the int shortcut); a generated repository names the one its table's key has. </summary>
public class GenericRepo<T, TKey> : IGenericRepo<T, TKey>, IDisposable where T : BaseEntity where TKey : notnull
{
    protected readonly ShopContext _context;
    protected readonly DbSet<T> _dbSet;
    private bool _disposed = false;

    public GenericRepo(ShopContext context)
    {
        _context = context;
        _dbSet = _context.Set<T>();
    }

    public T GetById(TKey id) => _dbSet.Find(id)!;

    public async Task<T?> GetByIdAsync(TKey id) => await _dbSet.FindAsync(id);

    public async Task<bool> ExistsAsync(TKey id)
    {
        // EF.Property<TKey>(e, keyName) == id, built as an expression because == is not defined for an open generic type
        string keyName = _context.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;
        var row = Expression.Parameter(typeof(T), "e");
        var key = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TKey)], row, Expression.Constant(keyName));
        var sameKey = Expression.Lambda<Func<T, bool>>(Expression.Equal(key, Expression.Constant(id, typeof(TKey))), row);
        return await _dbSet.AsNoTracking().AnyAsync(sameKey);
    }

    public T Get(Expression<Func<T, bool>> predicate) => _dbSet.FirstOrDefault(predicate)!;

    public async Task<T?> GetAsync(Expression<Func<T, bool>> predicate) => await _dbSet.FirstOrDefaultAsync(predicate);

    public async Task<List<T>> GetAll() => await _dbSet.ToListAsync();

    public async Task<List<T>> GetAllOrderBy<TOrder>(Expression<Func<T, TOrder>> key) => await _dbSet.OrderBy(key).ToListAsync();

    /// <summary> Newest first; takes a non-nullable DateTime column (what the generated API classes sort by). </summary>
    public async Task<List<T>> GetAllOrderByDescending(Expression<Func<T, DateTime>> key) => await _dbSet.OrderByDescending(key).ToListAsync();

    public async Task<bool> AddAsync(T newRow)
    {
        _dbSet.Add(newRow);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<T> UpdateAsync(TKey id, T rowToUpdate)
    {
        _context.Entry(rowToUpdate).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return await GetByIdAsync(id) ?? throw new InvalidOperationException($"Entity with id {id} not found.");
    }

    /// <summary> 0 = deleted, -1 = row no longer exists, 1 = blocked by a foreign key elsewhere ("in use"). </summary>
    public async Task<int> DeleteAsync(string deleteFromTable, TKey deleteId)
    {
        var rowToDelete = GetById(deleteId);
        if (rowToDelete is null)
            return -1;

        _dbSet.Remove(rowToDelete);
        try
        {
            await _context.SaveChangesAsync();
            return 0;
        }
        catch (DbUpdateException)
        {
            _context.Entry(rowToDelete).State = EntityState.Unchanged;
            return 1;
        }
    }

    public int Count() => _dbSet.Count();

    public async Task<int> CountAsync() => await _dbSet.CountAsync();

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
                _context.Dispose();
        }
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary> The repository of a table with an int key, the common case: every generated repository of such a table derives from this. A table with another key type derives from <c>GenericRepo&lt;T, TKey&gt;</c>. </summary>
public class GenericRepo<T> : GenericRepo<T, int>, IGenericRepo<T> where T : BaseEntity
{
    public GenericRepo(ShopContext context) : base(context)
    {
    }
}
