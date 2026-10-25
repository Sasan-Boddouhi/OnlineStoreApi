using System.Linq.Expressions;

namespace Application.Common.Queries;

public sealed class SortDefinition<TEntity>
{
    public required LambdaExpression KeySelector { get; init; }

    public bool Descending { get; init; }
}