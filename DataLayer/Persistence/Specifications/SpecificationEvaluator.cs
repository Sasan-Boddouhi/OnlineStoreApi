using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Specifications;

namespace DataLayer.Persistence.Specifications
{
    public static class SpecificationEvaluator<T> where T : class
    {
        private static readonly ConcurrentDictionary<string, MethodInfo> _orderMethodCache = new();

        public static IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec)
        {
            var query = inputQuery;

            if (spec.Criteria != null)
                query = query.Where(spec.Criteria);

            query = spec.Includes.Aggregate(query, (current, include) => current.Include(include));

            if (spec.IsReadOnly)
                query = query.AsNoTracking();

            if (spec.OrderExpressions?.Any() == true)
            {
                IOrderedQueryable<T>? orderedQuery = null;
                foreach (var (keySelector, descending) in spec.OrderExpressions)
                {
                    orderedQuery = ApplyOrdering(query, keySelector, descending, orderedQuery);
                }
                if (orderedQuery != null)
                    query = orderedQuery;
            }

            if (spec.IsPagingEnabled)
                query = query.Skip(spec.Skip!.Value).Take(spec.Take!.Value);

            return query;
        }

        private static IOrderedQueryable<T> ApplyOrdering(
            IQueryable<T> source,
            LambdaExpression keySelector,
            bool descending,
            IOrderedQueryable<T>? existingOrderedQuery)
        {
            var methodName = existingOrderedQuery == null
                ? (descending ? "OrderByDescending" : "OrderBy")
                : (descending ? "ThenByDescending" : "ThenBy");

            var entityType = typeof(T);
            var keyType = keySelector.ReturnType;
            var method = GetOrderMethod(entityType, keyType, methodName);

            var result = method.Invoke(null, new object[] { existingOrderedQuery ?? source, keySelector });
            return (IOrderedQueryable<T>)result!;
        }

        private static MethodInfo GetOrderMethod(Type entityType, Type keyType, string methodName)
        {
            var cacheKey = $"{entityType.FullName}|{keyType.FullName}|{methodName}";
            return _orderMethodCache.GetOrAdd(cacheKey, _ =>
            {
                return typeof(Queryable)
                    .GetMethods()
                    .First(m => m.Name == methodName && m.GetParameters().Length == 2)
                    .MakeGenericMethod(entityType, keyType);
            });
        }
    }
}