using Application.Interfaces;
using DataLayer.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Services;

public class SqlQueryService : ISqlQueryService
{
    private readonly AppDbContext _context;

    public SqlQueryService(AppDbContext context) => _context = context;

    public async Task<IReadOnlyList<TResult>> QueryAsync<TResult>(
        string sql,
        CancellationToken cancellationToken = default,
        params (string Name, object Value)[] parameters)
        where TResult : class, ISqlResult
    {
        var sqlParams = parameters
            .Select(p => new SqlParameter(p.Name, p.Value))
            .ToArray();

        return await _context.Database
            .SqlQueryRaw<TResult>(sql, sqlParams)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default,
        params (string Name, object Value)[] parameters)
    {
        var sqlParams = parameters
            .Select(p => new SqlParameter(p.Name, p.Value))
            .ToArray();

        return await _context.Database
            .ExecuteSqlRawAsync(sql, sqlParams, cancellationToken);
    }
}