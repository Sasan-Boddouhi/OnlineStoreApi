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
        if (string.IsNullOrWhiteSpace(sort))
            return ParseResult<List<ISortDefinition<TEntity>>>.Ok(new List<ISortDefinition<TEntity>>());

        var allowedSet = BuildAllowedSet(context);
        var parts = sort.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var (definitions, errors) = ParseParts<TEntity>(parts, allowedSet);

        return errors.Any()
            ? ParseResult<List<ISortDefinition<TEntity>>>.Fail(errors.ToArray())
            : ParseResult<List<ISortDefinition<TEntity>>>.Ok(definitions);
    }

    private static HashSet<string>? BuildAllowedSet<TEntity>(QueryParseContext<TEntity> context) where TEntity : class
    {
        if (context.AllowedFields?.Any() != true) return null;
        return new HashSet<string>(
            context.AllowedFields,
            context.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private static (List<ISortDefinition<TEntity>>, List<ParseError>) ParseParts<TEntity>(
        string[] parts,
        HashSet<string>? allowedSet)
        where TEntity : class
    {
        var definitions = new List<ISortDefinition<TEntity>>();
        var errors = new List<ParseError>();
        var param = Expression.Parameter(typeof(TEntity), "x");

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var (field, descending) = ParseFieldAndDirection(trimmed, errors);
            if (field is null) continue; // خطا قبلاً ثبت شده

            if (allowedSet is not null && !allowedSet.Contains(field))
            {
                errors.Add(new ParseError { Code = "invalid_sort_field", Message = $"Field '{field}' is not allowed for sorting.", Target = "sort" });
                continue;
            }

            var propertyExpression = BuildPropertyExpression(param, field, errors);
            if (propertyExpression is null) continue;

            var lambda = Expression.Lambda(propertyExpression, param);
            definitions.Add(new LambdaSortDefinition<TEntity>(lambda, descending));
        }

        return (definitions, errors);
    }

    private static (string? field, bool descending) ParseFieldAndDirection(string part, List<ParseError> errors)
    {
        var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rawField = tokens[0];
        bool descending = rawField.StartsWith('-');
        string field = descending ? rawField[1..] : rawField;

        if (!descending && tokens.Length > 1 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase))
            descending = true;

        if (string.IsNullOrWhiteSpace(field))
        {
            errors.Add(new ParseError { Code = "invalid_sort_field", Message = "Sort field is invalid.", Target = "sort" });
            return (null, false);
        }

        return (field, descending);
    }

    private static Expression? BuildPropertyExpression(ParameterExpression param, string field, List<ParseError> errors)
    {
        Expression property = param;
        foreach (var prop in field.Split('.'))
        {
            var propInfo = property.Type.GetProperty(prop,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            if (propInfo is null)
            {
                errors.Add(new ParseError { Code = "unknown_property", Message = $"Property '{prop}' not found on type '{property.Type.Name}'.", Target = "sort" });
                return null;
            }
            property = Expression.Property(property, propInfo);
        }
        return property;
    }
}