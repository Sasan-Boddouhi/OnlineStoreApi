using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Application.Common.Specifications;

public static class SpecificationEvaluator<TEntity>
    where TEntity : class
{
    private static readonly ConcurrentDictionary<string, MethodInfo> OrderMethodCache = new();

    public static IQueryable<TEntity> GetQuery(
        IQueryable<TEntity> inputQuery,
        ISpecification<TEntity> spec)
    {
        var query = inputQuery;

        if (spec.Criteria != null)
            query = query.Where(spec.Criteria);

        query = spec.Includes.Aggregate(
            query,
            (current, include) => current.Include(include));

        if (spec.IsReadOnly)
            query = query.AsNoTracking();

        if (spec.OrderExpressions.Any())
        {
            IOrderedQueryable<TEntity>? orderedQuery = null;

            foreach (var (keySelector, descending) in spec.OrderExpressions)
            {
                orderedQuery = ApplyOrdering(
                    orderedQuery ?? query,
                    keySelector,
                    descending,
                    orderedQuery != null);
            }

            query = orderedQuery ?? query;
        }

        if (spec.IsPagingEnabled)
            query = query
                .Skip(spec.Skip!.Value)
                .Take(spec.Take!.Value);

        return query;
    }

    private static IOrderedQueryable<TEntity> ApplyOrdering(
        IQueryable<TEntity> source,
        LambdaExpression keySelector,
        bool descending,
        bool isThenBy)
    {
        var methodName = isThenBy
            ? (descending ? "ThenByDescending" : "ThenBy")
            : (descending ? "OrderByDescending" : "OrderBy");

        var method = GetOrderMethod(
            typeof(TEntity),
            keySelector.ReturnType,
            methodName);

        return (IOrderedQueryable<TEntity>)method.Invoke(
            null,
            [source, keySelector])!;
    }

    private static MethodInfo GetOrderMethod(
        Type entityType,
        Type keyType,
        string methodName)
    {
        var cacheKey = $"{entityType.FullName}:{keyType.FullName}:{methodName}";

        return OrderMethodCache.GetOrAdd(cacheKey, _ =>
        {
            return typeof(Queryable)
                .GetMethods()
                .First(m =>
                    m.Name == methodName &&
                    m.GetParameters().Length == 2)
                .MakeGenericMethod(entityType, keyType);
        });
    }
}