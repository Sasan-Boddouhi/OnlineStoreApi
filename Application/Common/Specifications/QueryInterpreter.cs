using System.Linq.Expressions;
using Application.Common.Helpers;

namespace Application.Common.Specifications;

internal static class QueryInterpreter
{
    public static Expression<Func<T, bool>>? ParseFilter<T>(
        string? filter,
        IReadOnlyCollection<string> allowedFields) where T : class
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        return new FilterParser().Parse<T>(filter, allowedFields.ToList());
    }

    public static List<(Expression<Func<T, object>> expr, bool desc)> ParseSort<T>(
        string? sort,
        IReadOnlyCollection<string> allowedFields) where T : class
    {
        if (string.IsNullOrWhiteSpace(sort))
            return new();

        var list = new List<(Expression<Func<T, object>> expr, bool desc)>();
        var raw = SortParser.Parse<T>(sort, allowedFields);
        foreach (var (lambda, descending) in raw)
        {
            var param = Expression.Parameter(typeof(T), "x");
            var invoked = Expression.Invoke(lambda, param);
            var converted = Expression.Convert(invoked, typeof(object));
            var expr = Expression.Lambda<Func<T, object>>(converted, param);
            list.Add((expr, descending));
        }
        return list;
    }
}