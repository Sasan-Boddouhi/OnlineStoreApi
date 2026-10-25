using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Application.Common.Queries
{
    public static class StringQueryParser
    {
        public static ParseResult<QueryContract<TEntity>> TryParse<TEntity>(
            string? filter,
            string? sort,
            QueryParseContext<TEntity> context,
            int? page = null,
            int? size = null,
            int? skip = null,
            int? take = null)
            where TEntity : class
        {
            var errors = new List<ParseError>();

            Expression<Func<TEntity, bool>>? filterExpr = null;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                var parser = new FilterParser();

                var filterResult =
                    parser.TryParse<TEntity>(filter, context);

                if (!filterResult.Success)
                {
                    errors.AddRange(filterResult.Errors);
                }
                else
                {
                    filterExpr = filterResult.Value!;
                }
            }

            var sortDefs = new List<ISortDefinition<TEntity>>();

            if (!string.IsNullOrWhiteSpace(sort))
            {
                var sortResult =
                    SortParser.TryParse<TEntity>(sort, context);

                if (!sortResult.Success)
                {
                    errors.AddRange(sortResult.Errors);
                }
                else
                {
                    sortDefs = sortResult.Value!;
                }
            }

            if (errors.Any())
            {
                return ParseResult<QueryContract<TEntity>>
                    .Fail(errors.ToArray());
            }

            return ParseResult<QueryContract<TEntity>>
                .Ok(new QueryContract<TEntity>
                {
                    Filter = filterExpr,
                    Sorts = sortDefs.ToArray(),

                    Page = page,
                    Size = size,

                    Skip = skip,
                    Take = take
                });
        }
    }
}