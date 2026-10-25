using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Application.Common.Specifications;

public static class SpecificationEvaluator<TEntity>
    where TEntity : class
{
    private static readonly ConcurrentDictionary<string, MethodInfo> OrderMethodCache = new();

    private static IQueryable<TEntity> IncludeByExpression(
        IQueryable<TEntity> source,
        LambdaExpression expression)
    {
        var entityType = typeof(TEntity);
        var propertyType = expression.ReturnType;

        // پیدا کردن متد Include<TEntity, TProperty>
        var includeMethod = typeof(EntityFrameworkQueryableExtensions)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(m => m.Name == "Include" &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[1].ParameterType.GenericTypeArguments.Length == 1 &&
                        m.GetParameters()[1].ParameterType.GenericTypeArguments[0]
                            .GenericTypeArguments.Length == 2)
            .MakeGenericMethod(entityType, propertyType);

        return (IQueryable<TEntity>)includeMethod.Invoke(
            null,
            new object[] { source, expression })!;
    }

    // ───── GetQuery بدون تغییر در منطق ─────
    public static IQueryable<TEntity> GetQuery(
        IQueryable<TEntity> inputQuery,
        ISpecification<TEntity> spec)
    {
        var query = inputQuery;

        if (spec.Criteria != null)
            query = query.Where(spec.Criteria);

        // اعمال Include‌ها
        foreach (var include in spec.Includes)
        {
            query = IncludeByExpression(query, include);
        }

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