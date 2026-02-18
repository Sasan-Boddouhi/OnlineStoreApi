using Application.Common.Specifications;
using System.Linq.Expressions;

namespace Application.Interfaces
{
    public interface IGenericRepository<TEntity> where TEntity : class
    {
        // Basic CRUD
        Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);
        Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
        Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
        void Update(TEntity entity);
        void UpdateRange(IEnumerable<TEntity> entities);
        void Delete(TEntity entity);
        void DeleteRange(IEnumerable<TEntity> entities);

        // Simple Queries
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
        Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

        // Specification (Entity Result)
        Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> spec, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> spec, CancellationToken cancellationToken = default);
        Task<int> CountAsync(ISpecification<TEntity> spec, CancellationToken cancellationToken = default);

        // Projection Specification (DTO Result)
        Task<IReadOnlyList<TResult>> ListAsync<TResult>(
            IProjectionSpecification<TEntity, TResult> spec,
            CancellationToken cancellationToken = default);
    }
}
