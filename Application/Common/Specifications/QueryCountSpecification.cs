using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Application.Common.Specifications
{
    public class QueryCountSpecification<TEntity> : BaseSpecification<TEntity>
    {
        private static readonly ConcurrentDictionary<Type, bool> _hasIsActiveCache = new();

        public QueryCountSpecification(
            string? filter,
            string[] allowedFields,
            bool applyDefaultSoftDelete = true)
            : base(allowedFields)
        {
            if (applyDefaultSoftDelete)
            {
                bool hasIsActive = _hasIsActiveCache.GetOrAdd(typeof(TEntity), t =>
                {
                    var prop = t.GetProperty("IsActive", BindingFlags.Public | BindingFlags.Instance);
                    return prop != null && prop.PropertyType == typeof(bool);
                });

                if (hasIsActive)
                {
                    var parameter = Expression.Parameter(typeof(TEntity), "x");
                    var property = Expression.Property(parameter, "IsActive");
                    var condition = Expression.Lambda<Func<TEntity, bool>>(
                        Expression.Equal(property, Expression.Constant(true)), parameter);
                    AddCriteria(condition);
                }
            }

            if (!string.IsNullOrWhiteSpace(filter))
                ApplyFilterString(filter);
        }
    }
}