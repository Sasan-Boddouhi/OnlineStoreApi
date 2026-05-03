// Application/Common/Helpers/SortParser.cs
using System.Linq.Expressions;

namespace Application.Common.Helpers;

public static class SortParser
{
    public static List<(LambdaExpression Lambda, bool Descending)> Parse<TEntity>(
        string? sort,
        IReadOnlyCollection<string>? allowedFields = null)
    {
        var result = new List<(LambdaExpression, bool)>();
        if (string.IsNullOrWhiteSpace(sort))
            return result;

        foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var field = tokens[0];
            var descending = tokens.Length > 1 &&
                             tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            if (allowedFields?.Count > 0 && !allowedFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Field '{field}' is not allowed for sorting.");

            var param = Expression.Parameter(typeof(TEntity), "x");
            Expression property = param;
            foreach (var prop in field.Split('.'))
            {
                property = Expression.Property(property, prop);
            }
            var lambda = Expression.Lambda(property, param);
            result.Add((lambda, descending));
        }
        return result;
    }
}