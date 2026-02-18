using System.Linq.Expressions;

namespace Application.Common.Specifications;

public abstract class BaseSpecification<TEntity>
    : ISpecification<TEntity>
{
    private readonly List<Expression<Func<TEntity, object>>> _includes = new();
    public IReadOnlyList<Expression<Func<TEntity, object>>> Includes => _includes;

    protected void AddInclude(Expression<Func<TEntity, object>> includeExpression)
        => _includes.Add(includeExpression);
    public Expression<Func<TEntity, bool>>? Criteria { get; protected set; }

    public Expression<Func<TEntity, object>>? OrderBy { get; protected set; }
    public Expression<Func<TEntity, object>>? OrderByDescending { get; protected set; }

    public int? Skip { get; protected set; }
    public int? Take { get; protected set; }

    public bool IsPagingEnabled => Skip.HasValue && Take.HasValue;

    protected void AddCriteria(Expression<Func<TEntity, bool>> criteria)
    {
        if (Criteria == null)
        {
            Criteria = criteria;
        }
        else
        {
            // Combine with AND
            var parameter = Expression.Parameter(typeof(TEntity));

            var leftVisitor = new ReplaceExpressionVisitor(
                criteria.Parameters[0], parameter);

            var left = leftVisitor.Visit(criteria.Body);

            var rightVisitor = new ReplaceExpressionVisitor(
                Criteria.Parameters[0], parameter);

            var right = rightVisitor.Visit(Criteria.Body);

            Criteria = Expression.Lambda<Func<TEntity, bool>>(
                Expression.AndAlso(right!, left!),
                parameter);
        }
    }

    protected void ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
    }

    protected void ApplyOrderBy(Expression<Func<TEntity, object>> orderByExpression)
        => OrderBy = orderByExpression;

    protected void ApplyOrderByDescending(Expression<Func<TEntity, object>> orderByDescExpression)
        => OrderByDescending = orderByDescExpression;
}
