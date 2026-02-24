using Application.Common.Queries;
using System.Linq.Expressions;
using System.Reflection;

namespace Application.Common.Specifications;

public class DynamicSpecification<TEntity> : BaseSpecification<TEntity>
    where TEntity : class
{
    private readonly HashSet<string> _allowedFields;

    public DynamicSpecification(
        QueryRequest request,
        IEnumerable<string>? allowedFields = null)
    {
        _allowedFields = allowedFields != null
            ? new HashSet<string>(allowedFields)
            : new HashSet<string>();

        // Filters (AND)
        if (request.Filters?.Any() == true)
        {
            foreach (var filter in request.Filters)
            {
                ValidateField(filter.Field);

                var expression = BuildExpression(filter);
                AddCriteria(expression);
            }
        }

        // Global Search (OR)
        if (!string.IsNullOrWhiteSpace(request.Search)
            && request.SearchFields?.Any() == true)
        {
            var searchExpression =
                BuildSearchExpression(request.Search, request.SearchFields);

            AddCriteria(searchExpression);
        }

        // Sorting
        if (!string.IsNullOrWhiteSpace(request.SortBy))
        {
            ValidateField(request.SortBy);
            ApplyDynamicSorting(request.SortBy, request.Ascending);
        }

        // Paging
        ApplyPaging(
            (request.PageNumber - 1) * request.PageSize,
            request.PageSize);
    }

    // ===============================
    // Expression Builders
    // ===============================

    private Expression<Func<TEntity, bool>> BuildExpression(FilterRequest filter)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");

        var property = BuildNestedProperty(parameter, filter.Field);

        var targetType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;

        var convertedValue = ConvertToType(filter.Value, targetType);

        var constant = Expression.Constant(convertedValue, targetType);

        Expression propertyAccess = property.Type != targetType
            ? Expression.Convert(property, targetType)
            : property;

        Expression body = filter.Operator.ToLower() switch
        {
            "eq" => Expression.Equal(propertyAccess, constant),

            "neq" => Expression.NotEqual(propertyAccess, constant),

            "gt" => Expression.GreaterThan(propertyAccess, constant),

            "gte" => Expression.GreaterThanOrEqual(propertyAccess, constant),

            "lt" => Expression.LessThan(propertyAccess, constant),

            "lte" => Expression.LessThanOrEqual(propertyAccess, constant),

            "contains" => Expression.Call(
                propertyAccess,
                typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                constant),

            "in" => BuildInExpression(propertyAccess, filter.Value),

            _ => throw new NotSupportedException($"Operator '{filter.Operator}' not supported")
        };

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private Expression<Func<TEntity, bool>> BuildSearchExpression(
        string search,
        IEnumerable<string> fields)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");

        Expression? orExpression = null;

        foreach (var field in fields)
        {
            ValidateField(field);

            var property = BuildNestedProperty(parameter, field);

            var contains = Expression.Call(
                property,
                typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                Expression.Constant(search));

            orExpression = orExpression == null
                ? contains
                : Expression.OrElse(orExpression, contains);
        }

        return Expression.Lambda<Func<TEntity, bool>>(orExpression!, parameter);
    }

    // ===============================
    // Helpers
    // ===============================

    private MemberExpression BuildNestedProperty(
        Expression parameter,
        string propertyPath)
    {
        var properties = propertyPath.Split('.');
        Expression current = parameter;

        foreach (var prop in properties)
        {
            current = Expression.Property(current, prop);
        }

        return (MemberExpression)current;
    }

    private object? ConvertToType(object? value, Type targetType)
    {
        if (value == null)
            return null;

        if (targetType.IsEnum)
            return Enum.Parse(targetType, value.ToString()!);

        return Convert.ChangeType(value, targetType);
    }

    private Expression BuildInExpression(
        Expression property,
        object? value)
    {
        if (value is not IEnumerable<object> values)
            throw new ArgumentException("IN operator requires array");

        var constant = Expression.Constant(values);

        return Expression.Call(
            typeof(Enumerable),
            "Contains",
            new[] { property.Type },
            constant,
            property);
    }

    private void ApplyDynamicSorting(string sortBy, bool ascending)
    {
        ApplyOrderBy(sortBy, !ascending);
    }

    private void ValidateField(string field)
    {
        if (_allowedFields.Count == 0)
            return;

        if (!_allowedFields.Contains(field))
            throw new Exception($"Field '{field}' is not allowed.");
    }
}
