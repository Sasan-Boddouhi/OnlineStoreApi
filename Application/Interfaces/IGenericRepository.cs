using Application.Common.Specifications;
using System.Linq.Expressions;

public interface IGenericRepository<TEntity> where TEntity : class
{
    // CRUD ...
    Task<TEntity?> GetByIdAsync(object id, CancellationToken ct = default);
    Task AddAsync(TEntity entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);
    void Update(TEntity entity);
    void UpdateRange(IEnumerable<TEntity> entities);
    void Delete(TEntity entity);
    void DeleteRange(IEnumerable<TEntity> entities);

    // Simple queries
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default);

    // Specification queries (entity)
    Task<TEntity?> FirstOrDefaultAsync(Spec<TEntity> spec, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAsync(Spec<TEntity> spec, CancellationToken ct = default);
    Task<int> CountAsync(Spec<TEntity> spec, CancellationToken ct = default);
    Task<bool> AnyAsync(Spec<TEntity> spec, CancellationToken ct = default);

    // Projected queries (DTO)
    Task<TResult?> FirstOrDefaultAsync<TResult>(
        Spec<TEntity> spec,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken ct = default) where TResult : class;

    Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        Spec<TEntity> spec,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken ct = default) where TResult : class;
}