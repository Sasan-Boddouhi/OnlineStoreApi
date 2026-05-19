// Application/Common/Helpers/SortParser.cs
using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Queries.Internal;

namespace Application.Common.Queries;

public static class SortParser
{
    public static ParseResult<List<ISortDefinition<TEntity>>> TryParse<TEntity>(
        string? sort,
        QueryParseContext<TEntity> context)   // context generic
        where TEntity : class
    {
        var errors = new List<ParseError>();
        var result = new List<ISortDefinition<TEntity>>();
        if (string.IsNullOrWhiteSpace(sort))
            return ParseResult<List<ISortDefinition<TEntity>>>.Ok(result);

        var allowedSet = context.AllowedFields?.Any() == true
            ? new HashSet<string>(context.AllowedFields,
                context.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : null)
            : null;

        foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var field = tokens[0];
            var descending = tokens.Length > 1 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

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
            foreach (var prop in field.Split('.'))
            {
                var propInfo = property.Type.GetProperty(prop,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                if (propInfo == null)
                {
                    errors.Add(new ParseError
                    {
                        Code = "unknown_property",
                        Message = $"Property '{prop}' not found on type '{property.Type.Name}'.",
                        Target = "sort"
                    });
                    break;
                }
                property = Expression.Property(property, propInfo);
            }
            if (property == param) continue; // skip if error occurred

            var lambda = Expression.Lambda(property, param);
            result.Add(new LambdaSortDefinition<TEntity>(lambda, descending));
        }

        if (errors.Any())
            return ParseResult<List<ISortDefinition<TEntity>>>.Fail(errors.ToArray());

        return ParseResult<List<ISortDefinition<TEntity>>>.Ok(result);
    }
}