// Application/Common/Helpers/SortParser.cs

using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Queries.Internal;

namespace Application.Common.Queries;

public static class SortParser
{
    public static ParseResult<List<ISortDefinition<TEntity>>> TryParse<TEntity>(
        string? sort,
        QueryParseContext<TEntity> context)
        where TEntity : class
    {
        var errors = new List<ParseError>();
        var result = new List<ISortDefinition<TEntity>>();

        if (string.IsNullOrWhiteSpace(sort))
            return ParseResult<List<ISortDefinition<TEntity>>>.Ok(result);

        var allowedSet = context.AllowedFields?.Any() == true
            ? new HashSet<string>(
                context.AllowedFields,
                context.CaseInsensitive
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
            : null;

        foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var rawField = tokens[0];

            // support: -field
            var descending = rawField.StartsWith('-');

            var field = descending
                ? rawField[1..]
                : rawField;

            // support: field desc
            if (!descending &&
                tokens.Length > 1 &&
                tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase))
            {
                descending = true;
            }

            if (string.IsNullOrWhiteSpace(field))
            {
                errors.Add(new ParseError
                {
                    Code = "invalid_sort_field",
                    Message = "Sort field is invalid.",
                    Target = "sort"
                });

                continue;
            }

            if (allowedSet != null && !allowedSet.Contains(field))
            {
                errors.Add(new ParseError
                {
                    Code = "invalid_sort_field",
                    Message = $"Field '{field}' is not allowed for sorting.",
                    Target = "sort"
                });

                continue;
            }

            var param = Expression.Parameter(typeof(TEntity), "x");

            Expression property = param;

            var hasError = false;

            foreach (var prop in field.Split('.'))
            {
                var propInfo = property.Type.GetProperty(
                    prop,
                    BindingFlags.IgnoreCase |
                    BindingFlags.Public |
                    BindingFlags.Instance);

                if (propInfo == null)
                {
                    errors.Add(new ParseError
                    {
                        Code = "unknown_property",
                        Message = $"Property '{prop}' not found on type '{property.Type.Name}'.",
                        Target = "sort"
                    });

                    hasError = true;
                    break;
                }

                property = Expression.Property(property, propInfo);
            }

            if (hasError)
                continue;

            var lambda = Expression.Lambda(property, param);

            result.Add(new LambdaSortDefinition<TEntity>(lambda, descending));
        }

        if (errors.Any())
            return ParseResult<List<ISortDefinition<TEntity>>>.Fail(errors.ToArray());

        return ParseResult<List<ISortDefinition<TEntity>>>.Ok(result);
    }
}