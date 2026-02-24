using Application.Common.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Specifications
{
    public abstract class BaseProjectionSpecification<TEntity, TResult> : BaseSpecification<TEntity>, IProjectionSpecification<TEntity, TResult>
    {
        protected BaseProjectionSpecification(
            IEnumerable<string>? allowedFields = null)
            : base(allowedFields)
        {
        }

        public Expression<Func<TEntity, TResult>> Selector { get; protected set; } = default!;

        protected void SetProjection(
            Expression<Func<TEntity, TResult>> selector)
        {
            Selector = selector;
        }
    }

}
