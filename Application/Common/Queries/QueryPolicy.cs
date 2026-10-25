
namespace Application.Common.Queries;

public static class QueryPolicy
{
    // 1. Validate: فقط اعتبارسنجی، بدون تغییر در مدل
    public static ParseResult<QueryContract<TEntity>> Validate<TEntity>(
        QueryContract<TEntity> query,
        QueryParseContext<TEntity> context)
        where TEntity : class
    {
        var errors = new List<ParseError>();

        if (query.Page.HasValue && query.Page < 1)
            errors.Add(new ParseError { Code = "invalid_page", Message = "Page must be greater than 0.", Target = "page" });

        if (query.Size.HasValue && (query.Size < 1 || query.Size > context.MaxPageSize))
            errors.Add(new ParseError { Code = "invalid_size", Message = $"Size must be between 1 and {context.MaxPageSize}.", Target = "size" });

        if (query.Skip.HasValue && query.Skip < 0)
            errors.Add(new ParseError { Code = "invalid_skip", Message = "Skip must be >= 0.", Target = "skip" });

        if (query.Take.HasValue && (query.Take < 1 || query.Take > context.MaxPageSize))
            errors.Add(new ParseError { Code = "invalid_take", Message = $"Take must be between 1 and {context.MaxPageSize}.", Target = "take" });

        if (errors.Any())
            return ParseResult<QueryContract<TEntity>>.Fail(errors.ToArray());

        return ParseResult<QueryContract<TEntity>>.Ok(query);
    }

    // 2. Normalize: بازگرداندن یک QueryContract جدید با مقادیر نرمال‌شده (غیر مخرب)
    public static QueryContract<TEntity> Normalize<TEntity>(
        QueryContract<TEntity> query,
        QueryParseContext<TEntity> context)
        where TEntity : class
    {
        // اگر از Skip/Take استفاده شده باشد
        if (query.Skip.HasValue || query.Take.HasValue)
        {
            var skip = query.Skip ?? 0;
            var take = query.Take ?? context.MaxPageSize;
            if (take > context.MaxPageSize) take = context.MaxPageSize;
            if (skip < 0) skip = 0;

            return new QueryContract<TEntity>
            {
                Filter = query.Filter,
                Sorts = query.Sorts,
                Skip = skip,
                Take = take
            };
        }

        // حالت Page/Size
        var page = query.Page ?? 1;
        var size = query.Size ?? 20;
        if (page < 1) page = 1;
        if (size < 1) size = 1;
        if (size > context.MaxPageSize) size = context.MaxPageSize;

        return new QueryContract<TEntity>
        {
            Filter = query.Filter,
            Sorts = query.Sorts,
            Page = page,
            Size = size
        };
    }
}