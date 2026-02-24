using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Application.Common.Specifications
{
    public class QuerySpecification<TEntity, TDto> : BaseProjectionSpecification<TEntity, TDto>
    {
        private static readonly ConcurrentDictionary<Type, bool> _hasIsActiveCache = new();

        public QuerySpecification(
            string? filter,
            string? sort,
            int? skip,
            int? take,
            Expression<Func<TEntity, TDto>> projection,
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

            if (!string.IsNullOrWhiteSpace(sort))
                ApplySortString(sort);

            if (skip.HasValue && take.HasValue)
                ApplyPaging(skip.Value, take.Value);

            SetProjection(projection);
        }
    }
}