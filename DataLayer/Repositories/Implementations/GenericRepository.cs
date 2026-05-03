using Application.Common.Specifications;
using Application.Interfaces;
using DataLayer.Context;
using DataLayer.Persistence.Specifications;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DataLayer.Repositories.Implementations
{
    public class GenericRepository<TEntity> : IGenericRepository<TEntity> where TEntity : class
    {
        private readonly AppDbContext _context;
        private readonly DbSet<TEntity> _dbSet;

        public GenericRepository(AppDbContext context)
        {
            _context = context;
            _dbSet = context.Set<TEntity>();
        }

        #region CRUD
        public async Task<TEntity?> GetByIdAsync(object id, CancellationToken ct = default)
            => await _dbSet.FindAsync(new[] { id }, ct);

        public async Task AddAsync(TEntity entity, CancellationToken ct = default)
            => await _dbSet.AddAsync(entity, ct);

        public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
            => await _dbSet.AddRangeAsync(entities, ct);

        public void Update(TEntity entity) => _dbSet.Update(entity);
        public void UpdateRange(IEnumerable<TEntity> entities) => _dbSet.UpdateRange(entities);
        public void Delete(TEntity entity) => _dbSet.Remove(entity);
        public void DeleteRange(IEnumerable<TEntity> entities) => _dbSet.RemoveRange(entities);
        #endregion

        #region Simple Queries
        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
            => await _dbSet.AnyAsync(predicate, ct);

        public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)
            => predicate == null
                ? await _dbSet.CountAsync(ct)
                : await _dbSet.CountAsync(predicate, ct);
        #endregion

        #region Specification (Entity)
        public async Task<TEntity?> FirstOrDefaultAsync(Spec<TEntity> spec, CancellationToken ct = default)
        {
            var query = SpecificationEvaluator<TEntity>.GetQuery(_dbSet.AsQueryable(), spec);
            return await query.FirstOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<TEntity>> ListAsync(Spec<TEntity> spec, CancellationToken ct = default)
        {
            var query = SpecificationEvaluator<TEntity>.GetQuery(_dbSet.AsQueryable(), spec);
            return await query.ToListAsync(ct);
        }

        public async Task<int> CountAsync(Spec<TEntity> spec, CancellationToken ct = default)
        {
            IQueryable<TEntity> query = _dbSet.AsQueryable();

            if (spec.Criteria is not null)
                query = query.Where(spec.Criteria);

            query = query.AsNoTracking();

            return await query.CountAsync(ct);
        }

        public async Task<bool> AnyAsync(Spec<TEntity> spec, CancellationToken ct = default)
        {
            var query = SpecificationEvaluator<TEntity>.GetQuery(_dbSet.AsQueryable(), spec);
            return await query.AnyAsync(ct);
        }
        #endregion

        #region Projection (DTO)
        public async Task<TResult?> FirstOrDefaultAsync<TResult>(
            Spec<TEntity> spec,
            Expression<Func<TEntity, TResult>> selector,
            CancellationToken ct = default) where TResult : class
        {
            var query = SpecificationEvaluator<TEntity>
                .GetQuery(_dbSet.AsQueryable(), spec)
                .Select(selector);
            return await query.FirstOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
            Spec<TEntity> spec,
            Expression<Func<TEntity, TResult>> selector,
            CancellationToken ct = default) where TResult : class
        {
            var query = SpecificationEvaluator<TEntity>
                .GetQuery(_dbSet.AsQueryable(), spec)
                .Select(selector);
            return await query.ToListAsync(ct);
        }
        #endregion
    }
}