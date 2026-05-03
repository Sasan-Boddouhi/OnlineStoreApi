using Application.Common.Helpers;
using Application.Common.Queries;

namespace Application.Common.Specifications;

public static class QueryBuilder
{
    public static Spec<TEntity> BuildFromProfile<TEntity>(
        IQueryProfile<TEntity> profile,
        QueryContract query) where TEntity : class
    {
        QueryGuard.EnsureValid(query.Filter, query.Sort);

        var spec = new Spec<TEntity>();

        // 1. شرط پایه
        if (profile.BaseCriteria != null)
            spec.Where(profile.BaseCriteria);

        // 2. Includeهای پیش‌فرض
        foreach (var include in profile.Includes)
            spec.Include(include);

        // 3. فیلتر (تفسیر به QueryInterpreter واگذار شده)
        var filterExpr = QueryInterpreter.ParseFilter<TEntity>(query.Filter, profile.AllowedFields);
        if (filterExpr != null)
            spec.Where(filterExpr);

        // 4. مرتب‌سازی (تفسیر به QueryInterpreter واگذار شده)
        if (!string.IsNullOrWhiteSpace(query.Sort))
        {
            var sortList = QueryInterpreter.ParseSort<TEntity>(query.Sort, profile.AllowedFields);
            bool first = true;
            foreach (var (expr, desc) in sortList)
            {
                if (first)
                {
                    spec.OrderByFirst(expr, desc);
                    first = false;
                }
                else
                {
                    spec.ThenBy(expr, desc);
                }
            }
        }

        // 5. صفحه‌بندی
        var paging = query.ToPaging();
        if (paging.Skip.HasValue && paging.Take.HasValue)
            spec.SkipTake(paging.Skip.Value, paging.Take.Value);
        else
            spec.Page(paging.Page ?? 1, paging.Size ?? 20);

        return spec;
    }

    public static Spec<TEntity> BuildForCount<TEntity>(
        IQueryProfile<TEntity> profile,
        string? filter) where TEntity : class
    {
        var spec = new Spec<TEntity>();

        if (profile.BaseCriteria != null)
            spec.Where(profile.BaseCriteria);

        var filterExpr = QueryInterpreter.ParseFilter<TEntity>(filter, profile.AllowedFields);
        if (filterExpr != null)
            spec.Where(filterExpr);

        return spec;
    }
}