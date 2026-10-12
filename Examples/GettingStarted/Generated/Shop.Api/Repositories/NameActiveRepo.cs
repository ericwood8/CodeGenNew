using System.Linq.Expressions;
using Shop.Api.Data;
using Shop.Api.Entities;

namespace Shop.Api.Repositories;

/// <summary> Repository of a Name + IsActive table: GenericRepo's calls plus GetAllActive, GetByName and the duplicate-name rule. The name is trimmed on create and update; a row is a
/// duplicate when another active row has the same name (an inactive row never blocks a name, and an update that makes a row inactive is never refused). </summary>
public class NameActiveRepo<T> : INameActiveRepo<T>, IDisposable where T : BaseNameActiveEntity
{
    protected readonly ShopContext _context;
    protected readonly DbSet<T> _dbSet;
    private bool _disposed = false;

    public NameActiveRepo(ShopContext context)
    {
        _context = context;
        _dbSet = _context.Set<T>();
    }

    private string KeyName => _context.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;

    public async Task<T?> GetByIdAsync(int id) => await _dbSet.FindAsync(id);

    public async Task<bool> ExistsAsync(int id)
    {
        string keyName = KeyName;
        return await _dbSet.AsNoTracking().AnyAsync(e => EF.Property<int>(e, keyName) == id);
    }

    public async Task<T?> GetAsync(Expression<Func<T, bool>> predicate) => await _dbSet.FirstOrDefaultAsync(predicate);

    public async Task<List<T>> GetAll() => await _dbSet.OrderBy(d => d.Name).ToListAsync();

    public async Task<List<T>> GetAllActive() => await _dbSet.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync();

    /// <summary> The active rows whose name starts with the text. </summary>
    public async Task<List<T>> GetByName(string name) => await _dbSet.Where(d => d.Name.StartsWith(name) && d.IsActive).ToListAsync();

    public async Task<List<T>> GetListAsync(Expression<Func<T, bool>> predicate) => await _dbSet.Where(predicate).ToListAsync();

    public async Task<bool> AddAsync(T newRow)
    {
        newRow.Name = newRow.Name.Trim();
        if (newRow.IsActive && await IsDupOnCreateAsync(newRow.Name))
            return false;

        _dbSet.Add(newRow);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<T?> UpdateAsync(int id, T rowToUpdate)
    {
        rowToUpdate.Name = rowToUpdate.Name.Trim();
        if (rowToUpdate.IsActive && await IsDupOnUpdateAsync(id, rowToUpdate.Name))
            return null;

        _context.Entry(rowToUpdate).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return await GetByIdAsync(id) ?? throw new InvalidOperationException($"Entity of type {typeof(T).Name} with id {id} not found.");
    }

    /// <summary> 0 = deleted, -1 = row no longer exists, 1 = blocked by a foreign key elsewhere ("in use"). </summary>
    public async Task<int> DeleteAsync(string deleteFromTable, int deleteId)
    {
        var rowToDelete = await GetByIdAsync(deleteId);
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

    public async Task<int> CountAsync() => await _dbSet.CountAsync();

    /// <summary> True when an active row already has that name. </summary>
    public async Task<bool> IsDupOnCreateAsync(string newName)
    {
        string name = newName.Trim();
        return await _dbSet.AsNoTracking().AnyAsync(d => d.Name == name && d.IsActive);
    }

    /// <summary> True when a different active row already has that name. The row being renamed never counts against itself. </summary>
    public async Task<bool> IsDupOnUpdateAsync(int id, string newName)
    {
        string name = newName.Trim();
        string keyName = KeyName;
        return await _dbSet.AsNoTracking().AnyAsync(d => d.Name == name && d.IsActive && EF.Property<int>(d, keyName) != id);
    }

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
