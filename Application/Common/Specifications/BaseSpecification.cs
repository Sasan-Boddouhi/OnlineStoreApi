using Application.Common.Helpers;
using System.Linq.Expressions;

namespace Application.Common.Specifications;

public abstract class BaseSpecification<TEntity> : ISpecification<TEntity>
{

    private readonly HashSet<string> _allowedFields;
    private readonly List<(LambdaExpression KeySelector, bool Descending)> _orderExpressions = new();
    public IReadOnlyList<(LambdaExpression KeySelector, bool Descending)> OrderExpressions => _orderExpressions;

    protected BaseSpecification(IEnumerable<string>? allowedFields = null)
    {
        _allowedFields = allowedFields?.ToHashSet() ?? new HashSet<string>();
    }

    protected void ValidateField(string propertyPath)
    {
        if (_allowedFields.Any() && !_allowedFields.Contains(propertyPath))
            throw new InvalidOperationException($"Field '{propertyPath}' is not allowed for sorting.");
    }
    protected void ApplyOrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool descending = false)
    {
        _orderExpressions.Add((keySelector, descending));
    }

    public void ApplyOrderBy(string propertyPath, bool descending = false)
    {
        ValidateField(propertyPath);
        var lambda = ExpressionBuilder.BuildPropertyLambdaCached<TEntity>(propertyPath);
        _orderExpressions.Add((lambda, descending));
    }

    private readonly List<Expression<Func<TEntity, object>>> _includes = new();
    public IReadOnlyList<Expression<Func<TEntity, object>>> Includes => _includes;

    protected void AddInclude(Expression<Func<TEntity, object>> includeExpression)
        => _includes.Add(includeExpression);
    public Expression<Func<TEntity, bool>>? Criteria { get; protected set; }

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
    protected void ApplyFilterString(string filter)
    {
        var parser = new FilterParser();
        var expression = parser.Parse<TEntity>(filter, _allowedFields.ToList());
        AddCriteria(expression);
    }

    protected void ApplySortString(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return;

        SortStringParser.ApplySortString(this, sort);
    }
}
