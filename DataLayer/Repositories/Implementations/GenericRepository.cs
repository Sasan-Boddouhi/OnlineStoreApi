using Application.Common.Specifications;
using Application.Interfaces;
using DataLayer.Context;
using DataLayer.Persistence.Specifications;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DataLayer.Repositories.Implementations
{
    public class GenericRepository<TEntity> : IGenericRepository<TEntity>
        where TEntity : class
    {
        private readonly AppDbContext _context;
        private readonly DbSet<TEntity> _dbSet;

        public GenericRepository(AppDbContext context)
        {
            _context = context;
            _dbSet = context.Set<TEntity>();
        }

        #region CRUD

        public async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            => await _dbSet.FindAsync(new[] { id }, cancellationToken);

        public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
            => await _dbSet.AddAsync(entity, cancellationToken);

        public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => await _dbSet.AddRangeAsync(entities, cancellationToken);

        public void Update(TEntity entity)
            => _dbSet.Update(entity);

        public void UpdateRange(IEnumerable<TEntity> entities)
            => _dbSet.UpdateRange(entities);

        public void Delete(TEntity entity)
            => _dbSet.Remove(entity);

        public void DeleteRange(IEnumerable<TEntity> entities)
            => _dbSet.RemoveRange(entities);

        #endregion

        #region Simple Queries

        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
            => await _dbSet.AnyAsync(predicate, cancellationToken);

        public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            return predicate == null
                ? await _dbSet.CountAsync(cancellationToken)
                : await _dbSet.CountAsync(predicate, cancellationToken);
        }

        #endregion

        #region Specification (Entity)

        public async Task<TEntity?> FirstOrDefaultAsync(
            ISpecification<TEntity> spec,
            CancellationToken cancellationToken = default)
        {
            var query = SpecificationEvaluator<TEntity>
                .GetQuery(_dbSet.AsQueryable(), spec);

            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<TResult?> FirstOrDefaultAsync<TResult>(
            IProjectionSpecification<TEntity, TResult> spec,
            CancellationToken cancellationToken = default)
        {
            return await ProjectionSpecificationEvaluator
                .GetQuery(_dbSet.AsQueryable(), spec)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> spec, CancellationToken cancellationToken = default)
        {
            var query = SpecificationEvaluator<TEntity>.GetQuery(_dbSet.AsQueryable(), spec);
            return await query.ToListAsync(cancellationToken);
        }

        public async Task<int> CountAsync(ISpecification<TEntity> spec, CancellationToken cancellationToken = default)
        {
            var query = _dbSet.AsQueryable();

            if (spec.Criteria != null)
                query = query.Where(spec.Criteria);

            return await query.CountAsync(cancellationToken);
        }

        #endregion

        #region Projection Specification (DTO)

        public async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
            IProjectionSpecification<TEntity, TResult> spec,
            CancellationToken cancellationToken = default)
        {
            var query = ProjectionSpecificationEvaluator
                .GetQuery(_dbSet.AsQueryable(), spec);

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<bool> AnyAsync(ISpecification<TEntity> spec, CancellationToken cancellationToken = default)
        {
            var query = _dbSet.AsQueryable();

            if (spec.Criteria != null)
                query = query.Where(spec.Criteria);

            return await query.AnyAsync(cancellationToken);
        }

        #endregion
    }
}
