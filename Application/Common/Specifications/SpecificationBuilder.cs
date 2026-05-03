using Application.Common.Helpers;
using Application.Common.Queries;

namespace Application.Common.Specifications;

public static class SpecificationBuilder
{


    /*
    Validation Layers (separation of concerns):
    
    1. QueryContract.Validate()
       → Structural validation (null checks, range limits)
    
    2. QueryGuard.EnsureValid()
       → Semantic & security validation (complexity, injection prevention)
    
    3. SpecificationBuilder
       → Translation layer (QueryContract → Spec<T>)
    */
    public static Spec<TEntity> BuildFromQuery<TEntity>(
        string? filter,
        string? sort,
        Paging paging,
        IReadOnlyCollection<string>? allowedFields = null)
        where TEntity : class
    {
        var spec = new Spec<TEntity>();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var criteria = new FilterParser().Parse<TEntity>(
                filter, allowedFields?.ToList() ?? new List<string>());
            if (criteria != null)
                spec.Where(criteria);
        }

        var sorts = SortParser.Parse<TEntity>(sort, allowedFields);
        bool isFirst = true;
        foreach (var (lambda, descending) in sorts)
        {
            if (isFirst)
            {
                spec.OrderByFirst(lambda, descending);
                isFirst = false;
            }
            else
            {
                spec.ThenBy(lambda, descending);
            }
        }

        // 3. Paging
        if (paging.Skip.HasValue && paging.Take.HasValue)
            spec.SkipTake(paging.Skip.Value, paging.Take.Value);
        else if (paging.Page.HasValue && paging.Size.HasValue)
            spec.Page(paging.Page.Value, paging.Size.Value);

        return spec;
    }

    /// <summary>ساخت Spec مستقیماً از QueryContract استاندارد.</summary>
    public static Spec<TEntity> Build<TEntity>(
        QueryContract query,
        IReadOnlyCollection<string>? allowedFields = null)
        where TEntity : class
    {
        query.Validate();            // لایه اول: structural
        EnsureSafe(query);          // لایه دوم: semantic / security

        return BuildFromQuery<TEntity>(
            query.Filter,
            query.Sort,
            query.ToPaging(),
            allowedFields
        );
    }

    private static void EnsureSafe(QueryContract query)
    {
        QueryGuard.EnsureValid(query.Filter, query.Sort);
    }
}