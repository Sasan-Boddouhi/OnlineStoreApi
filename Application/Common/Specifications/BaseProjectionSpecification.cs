using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Specifications
{
    public abstract class BaseProjectionSpecification<TEntity, TResult>
        : IProjectionSpecification<TEntity, TResult>
    {
        public Expression<Func<TEntity, bool>>? Criteria { get; protected set; }

        public Expression<Func<TEntity, object>>? OrderBy { get; protected set; }
        public Expression<Func<TEntity, object>>? OrderByDescending { get; protected set; }

        public Expression<Func<TEntity, TResult>> Selector { get; protected set; } = default!;

        public int? Skip { get; protected set; }
        public int? Take { get; protected set; }

        protected void ApplyPaging(int skip, int take)
        {
            Skip = skip;
            Take = take;
        }

        protected void ApplyOrderBy(Expression<Func<TEntity, object>> orderByExpression)
            => OrderBy = orderByExpression;

        protected void ApplyOrderByDescending(Expression<Func<TEntity, object>> orderByDescExpression)
            => OrderByDescending = orderByDescExpression;
    }

}
