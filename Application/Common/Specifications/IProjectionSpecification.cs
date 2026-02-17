using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Specifications
{
    public interface IProjectionSpecification<TEntity, TResult>
    {
        Expression<Func<TEntity, bool>>? Criteria { get; }

        Expression<Func<TEntity, object>>? OrderBy { get; }
        Expression<Func<TEntity, object>>? OrderByDescending { get; }

        Expression<Func<TEntity, TResult>> Selector { get; }

        int? Skip { get; }
        int? Take { get; }
    }
}
