using Application.Interfaces;
using DataLayer.Context;
using DataLayer.Repositories.Implementations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;

namespace DataLayer.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _context;
        private IDbContextTransaction? _transaction;

        // Repository cache (Thread-safe)
        private readonly ConcurrentDictionary<Type, object> _repositories = new();

        public UnitOfWork(AppDbContext context)
        {
            _context = context;
        }

        // ================================
        // Generic Repository Access
        // ================================
        public IGenericRepository<TEntity> Repository<TEntity>()
            where TEntity : class
        {
            return (IGenericRepository<TEntity>)_repositories.GetOrAdd(
                typeof(TEntity),
                _ => new GenericRepository<TEntity>(_context)
            );
        }

        // ================================
        // Save Changes
        // ================================
        public async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }

        // ================================
        // Transaction Handling
        // ================================
        public async Task BeginTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            if (_transaction != null)
                return;

            _transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);
        }

        public async Task CommitTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            if (_transaction == null)
                return;

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                await _transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            if (_transaction == null)
                return;

            try
            {
                await _transaction.RollbackAsync(cancellationToken);
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        // ================================
        // Dispose
        // ================================
        public void Dispose()
        {
            _transaction?.Dispose();
            _context.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction != null)
                await _transaction.DisposeAsync();

            await _context.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
