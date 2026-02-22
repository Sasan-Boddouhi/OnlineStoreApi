using Application.Common.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Persistence.Specifications
{
    public static class ProjectionSpecificationEvaluator
    {
        public static IQueryable<TResult> GetQuery<TEntity, TResult>(
            IQueryable<TEntity> inputQuery,
            IProjectionSpecification<TEntity, TResult> spec)
            where TEntity : class
        {
            var query = inputQuery;

            if (spec.Criteria != null)
                query = query.Where(spec.Criteria);

            if (spec.OrderBy != null)
                query = query.OrderBy(spec.OrderBy);

            else if (spec.OrderByDescending != null)
                query = query.OrderByDescending(spec.OrderByDescending);

            if (spec.Skip.HasValue)
                query = query.Skip(spec.Skip.Value);

            if (spec.Take.HasValue)
                query = query.Take(spec.Take.Value);

            return query.Select(spec.Selector);
        }
    }

}
