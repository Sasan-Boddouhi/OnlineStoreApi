using Application.Common.Queries;
using System.Linq;

namespace Application.Common.Specifications;

public static class QueryContractMapper
{
    public static Spec<T> ToSpec<T>(this QueryContract<T> query) where T : class
    {
        var spec = new Spec<T>();

        if (query.Filter != null)
            spec.Where(query.Filter);

        foreach (var sort in query.Sorts)
        {
            spec.OrderExpressions.Add((sort.KeySelector, sort.Descending));
        }

        // 3. صفحه‌بندی
        if (query.Skip.HasValue && query.Take.HasValue)
        {
            spec.ApplyPaging(query.Skip.Value, query.Take.Value);
        }
        else if (query.Page.HasValue || query.Size.HasValue)
        {
            var page = query.Page ?? 1;
            var size = query.Size ?? 20;
            spec.ApplyPaging((page - 1) * size, size);
        }

        return spec;
    }
}