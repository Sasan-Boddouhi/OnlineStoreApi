using Application.Common.Specifications;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace DataLayer.Persistence.Specifications
{
    public static class ProjectionSpecificationEvaluator
    {
        private static readonly ConcurrentDictionary<string, MethodInfo> _orderMethodCache = new();

        public static IQueryable<TResult> GetQuery<TEntity, TResult>(
            IQueryable<TEntity> inputQuery,
            IProjectionSpecification<TEntity, TResult> spec)
        {
            var query = inputQuery;

            if (spec.Criteria != null)
                query = query.Where(spec.Criteria);

            if (spec.OrderExpressions?.Any() == true)
            {
                IOrderedQueryable<TEntity>? orderedQuery = null;

                foreach (var (keySelector, descending) in spec.OrderExpressions)
                {
                    orderedQuery = ApplyOrdering(query, keySelector, descending, orderedQuery);
                }

                if (orderedQuery != null)
                    query = orderedQuery;
            }

            if (spec.Skip.HasValue)
                query = query.Skip(spec.Skip.Value);

            if (spec.Take.HasValue)
                query = query.Take(spec.Take.Value);

            return query.Select(spec.Selector);
        }

        private static IOrderedQueryable<TEntity> ApplyOrdering<TEntity>(
            IQueryable<TEntity> source,
            LambdaExpression keySelector,
            bool descending,
            IOrderedQueryable<TEntity>? existingOrderedQuery)
        {
            var methodName = existingOrderedQuery == null
                ? (descending ? "OrderByDescending" : "OrderBy")
                : (descending ? "ThenByDescending" : "ThenBy");

            var entityType = typeof(TEntity);
            var keyType = keySelector.ReturnType;

            var method = GetOrderMethod(entityType, keyType, methodName);

            var result = method.Invoke(null, new object[]
            {
                existingOrderedQuery ?? source,
                keySelector
            });

            return (IOrderedQueryable<TEntity>)result!;
        }

        private static MethodInfo GetOrderMethod(Type entityType, Type keyType, string methodName)
        {
            var cacheKey = $"{entityType.FullName}|{keyType.FullName}|{methodName}";
            return _orderMethodCache.GetOrAdd(cacheKey, _ =>
            {
                return typeof(Queryable)
                    .GetMethods()
                    .First(m =>
                        m.Name == methodName &&
                        m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 2)
                    .MakeGenericMethod(entityType, keyType);
            });
        }
    }
}