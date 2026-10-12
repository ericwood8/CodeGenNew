using System.Linq.Expressions;

namespace Shop.Api.Repositories;

/// <summary> The repository of a table whose key is a <typeparamref name="TKey"/> (int, long, Guid or string). </summary>
public interface IGenericRepo<T, TKey> where T : BaseEntity where TKey : notnull
{
    public T GetById(TKey id);
    public Task<T?> GetByIdAsync(TKey id);
    public Task<bool> ExistsAsync(TKey id);
    public T Get(Expression<Func<T, bool>> predicate);
    public Task<T?> GetAsync(Expression<Func<T, bool>> predicate);

    public Task<List<T>> GetAll();

    public Task<bool> AddAsync(T newRow);

    public Task<T> UpdateAsync(TKey id, T rowToUpdate);

    public Task<int> DeleteAsync(string deleteFromTable, TKey deleteId);

    public int Count();
    public Task<int> CountAsync();
}

/// <summary> The repository of a table with an int key, the common case. </summary>
public interface IGenericRepo<T> : IGenericRepo<T, int> where T : BaseEntity
{
}
