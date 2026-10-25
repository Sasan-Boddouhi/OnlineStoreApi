using Application.Common.Queries;
using System.Linq.Expressions;

namespace  Application.Common.Queries;
public sealed class QueryContract<TEntity>
    where TEntity : class
{
    public Expression<Func<TEntity, bool>>? Filter { get; init; }

    public IReadOnlyList<ISortDefinition<TEntity>> Sorts { get; init; }
        = Array.Empty<ISortDefinition<TEntity>>();

    // Paging Mode 1
    public int? Page { get; init; }
    public int? Size { get; init; }

    // Paging Mode 2
    public int? Skip { get; init; }
    public int? Take { get; init; }
}