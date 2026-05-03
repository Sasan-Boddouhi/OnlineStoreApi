using System.Linq.Expressions;
using Application.Common.Helpers; // PredicateBuilder

namespace Application.Common.Specifications;

using OrderExpr = (System.Linq.Expressions.LambdaExpression KeySelector, bool Descending);

public class Spec<TEntity> : ISpecification<TEntity> where TEntity : class
{
    private readonly List<Expression<Func<TEntity, object>>> _includes = new();
    private readonly List<OrderExpr> _orderExpressions = new();
    private readonly List<string> _tags = new();
    private bool _isReadOnly;

    // ---------- Fluent Building ----------
    public Spec<TEntity> Where(Expression<Func<TEntity, bool>> criteria)
    {
        Criteria = Criteria is null ? criteria : Criteria.And(criteria);
        return this;
    }

    public Spec<TEntity> WhereIf(bool condition, Expression<Func<TEntity, bool>> criteria)
    {
        if (condition) Where(criteria);
        return this;
    }

    public Spec<TEntity> With(Spec<TEntity> other) => Combine(this, other);

    public static Spec<TEntity> Combine(Spec<TEntity> first, Spec<TEntity> second)
    {
        if (first.IsPagingEnabled && second.IsPagingEnabled)
            throw new InvalidOperationException(
                $"Cannot combine two paged specifications. " +
                $"First: Skip={first.Skip}, Take={first.Take} | " +
                $"Second: Skip={second.Skip}, Take={second.Take}");

        var result = first.Clone();

        // Criteria
        if (second.Criteria is not null)
            result.Where(second.Criteria);

        // Includes (بدون duplicate)
        foreach (var inc in second.Includes)
        {
            if (!result._includes.Contains(inc))
                result.Include(inc);
        }

        // Ordering: اگر دومی ترتیبی داشته باشد، جایگزین ترتیب اولی می‌شود
        if (second.OrderExpressions.Any())
        {
            result._orderExpressions.Clear();
            result._orderExpressions.AddRange(second.OrderExpressions);
        }

        // Paging: فقط اگر اولی صفحه‌بندی ندارد
        if (!first.IsPagingEnabled && second.IsPagingEnabled)
            result.SkipTake(second.Skip!.Value, second.Take!.Value);

        // Tracking
        if (second.IsReadOnly)
            result.AsNoTracking();

        // Tags
        result._tags.AddRange(second._tags);

        return result;
    }

    public Spec<TEntity> Clone()
    {
        var clone = new Spec<TEntity>();
        // Expressionها immutable هستند، shallow copy بی‌خطر است
        clone.Criteria = this.Criteria;
        foreach (var inc in _includes) clone._includes.Add(inc);
        foreach (var ord in _orderExpressions) clone._orderExpressions.Add(ord);
        clone.Skip = this.Skip;
        clone.Take = this.Take;
        clone._isReadOnly = this._isReadOnly;
        clone._tags.AddRange(this._tags);
        return clone;
    }

    // ---------- Ordering (LINQ style) ----------
    public Spec<TEntity> OrderByFirst<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool descending = false)
    {
        _orderExpressions.Clear();
        _orderExpressions.Add((keySelector, descending));
        return this;
    }

    public Spec<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool descending = false)
    {
        if (_orderExpressions.Count == 0)
            throw new InvalidOperationException("ThenBy cannot be used before OrderByFirst.");
        _orderExpressions.Add((keySelector, descending));
        return this;
    }

    // LambdaExpression overloads (for Builder)
    public Spec<TEntity> OrderByFirst(LambdaExpression keySelector, bool descending = false)
    {
        _orderExpressions.Clear();
        _orderExpressions.Add((keySelector, descending));
        return this;
    }

    public Spec<TEntity> ThenBy(LambdaExpression keySelector, bool descending = false)
    {
        if (_orderExpressions.Count == 0)
            throw new InvalidOperationException("ThenBy cannot be used before OrderByFirst.");
        _orderExpressions.Add((keySelector, descending));
        return this;
    }

    public Spec<TEntity> Include(Expression<Func<TEntity, object>> include)
    {
        _includes.Add(include);
        return this;
    }

    public Spec<TEntity> Page(int pageNumber, int pageSize)
    {
        Skip = (pageNumber - 1) * pageSize;
        Take = pageSize;
        return this;
    }   

    public Spec<TEntity> SkipTake(int skip, int take)
    {
        Skip = skip;
        Take = take;
        return this;
    }

    public Spec<TEntity> AsNoTracking()
    {
        _isReadOnly = true;
        return this;
    }

    public Spec<TEntity> AsTracking()
    {
        _isReadOnly = false;
        return this;
    }

    // ---------- Tagging ----------
    public Spec<TEntity> WithTag(string tag)
    {
        if (!_tags.Contains(tag))
            _tags.Add(tag);
        return this;
    }

    public IReadOnlyList<string> Tags => _tags;

    // ---------- ISpecification Implementation ----------
    public Expression<Func<TEntity, bool>>? Criteria { get; private set; }
    public IReadOnlyList<Expression<Func<TEntity, object>>> Includes => _includes;
    public IReadOnlyList<OrderExpr> OrderExpressions => _orderExpressions;
    public int? Skip { get; private set; }
    public int? Take { get; private set; }
    public bool IsPagingEnabled => Skip.HasValue && Take.HasValue;
    public bool IsReadOnly => _isReadOnly;
}