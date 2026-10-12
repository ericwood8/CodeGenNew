using System.Linq.Expressions;
using Shop.Api.Entities;

namespace Shop.Api.Repositories;

/// <summary> The repository of a Name + IsActive table (CS_Repo derives every such table's repository from NameActiveRepo). </summary>
public interface INameActiveRepo<T> where T : BaseNameActiveEntity
{
    public Task<T?> GetByIdAsync(int id);
    public Task<bool> ExistsAsync(int id);
    public Task<T?> GetAsync(Expression<Func<T, bool>> predicate);

    public Task<List<T>> GetAll();
    public Task<List<T>> GetAllActive();
    public Task<List<T>> GetByName(string name);
    public Task<List<T>> GetListAsync(Expression<Func<T, bool>> predicate);

    /// <summary> False when an active row already has that name. </summary>
    public Task<bool> AddAsync(T newRow);
    /// <summary> The saved row, or null when another active row already has that name. </summary>
    public Task<T?> UpdateAsync(int id, T rowToUpdate);
    /// <summary> 0 = deleted, -1 = row no longer exists, 1 = blocked by a foreign key elsewhere ("in use"), the same codes as GenericRepo. </summary>
    public Task<int> DeleteAsync(string deleteFromTable, int deleteId);

    public Task<bool> IsDupOnCreateAsync(string newName);
    public Task<bool> IsDupOnUpdateAsync(int id, string newName);

    public Task<int> CountAsync();
}
