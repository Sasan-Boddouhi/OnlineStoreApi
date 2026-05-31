using System.Linq.Expressions;

namespace Application.Common.Specifications;

public sealed class Spec<TEntity> : ISpecification<TEntity>
    where TEntity : class
{
    public Expression<Func<TEntity, bool>>? Criteria { get; private set; }

    public List<LambdaExpression> Includes { get; } = [];

    public List<(LambdaExpression KeySelector, bool Descending)> OrderExpressions { get; } = [];

    public int? Skip { get; private set; }
    public int? Take { get; private set; }
    public bool IsPagingEnabled => Skip.HasValue || Take.HasValue;
    public bool IsReadOnly { get; private set; } = true;
    public List<string> Tags { get; } = [];

    // ===== فیلتر =====
    public Spec<TEntity> Where(Expression<Func<TEntity, bool>> criteria)
    {
        if (Criteria == null)
        {
            Criteria = criteria;
            return this;
        }

        var parameter = Expression.Parameter(typeof(TEntity));

        var leftVisitor = new ReplaceExpressionVisitor(criteria.Parameters[0], parameter);
        var left = leftVisitor.Visit(criteria.Body)!;

        var rightVisitor = new ReplaceExpressionVisitor(Criteria.Parameters[0], parameter);
        var right = rightVisitor.Visit(Criteria.Body)!;

        Criteria = Expression.Lambda<Func<TEntity, bool>>(Expression.AndAlso(left, right), parameter);
        return this;
    }

    // ===== Include عمومی (تبدیل به Expression<Func<TEntity, object>>) =====
    public Spec<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> include)
    {
        Includes.Add(include);
        return this;
    }

    // ===== مرتب‌سازی =====
    public Spec<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool descending = false)
    {
        OrderExpressions.Add((keySelector, descending));
        return this;
    }

    public Spec<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        => OrderBy(keySelector, descending: true);

    public Spec<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool descending = false)
    {
        OrderExpressions.Add((keySelector, descending));
        return this;
    }

    public Spec<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        => ThenBy(keySelector, descending: true);

    // ===== صفحه‌بندی =====
    public Spec<TEntity> ApplyPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        return this;
    }

    // ===== Tracking =====
    public Spec<TEntity> AsTracking()
    {
        IsReadOnly = false;
        return this;
    }

    // ===== برچسب =====
    public Spec<TEntity> Tag(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag))
            Tags.Add(tag);
        return this;
    }
}