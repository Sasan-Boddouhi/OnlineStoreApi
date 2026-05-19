using System.Linq.Expressions;

namespace Application.Common.Specifications;

public interface ISpecification<TEntity>
    where TEntity : class
{
    Expression<Func<TEntity, bool>>? Criteria { get; }

    List<Expression<Func<TEntity, object>>> Includes { get; }

    List<(LambdaExpression KeySelector, bool Descending)> OrderExpressions { get; }

    int? Skip { get; }

    int? Take { get; }

    bool IsPagingEnabled { get; }

    bool IsReadOnly { get; }
}