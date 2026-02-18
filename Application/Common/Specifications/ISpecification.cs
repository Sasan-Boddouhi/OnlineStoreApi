using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Specifications
{
    public interface ISpecification<TEntity>
    {
        IReadOnlyList<Expression<Func<TEntity, object>>> Includes { get; }
        Expression<Func<TEntity, bool>>? Criteria { get; }

        Expression<Func<TEntity, object>>? OrderBy { get; }
        Expression<Func<TEntity, object>>? OrderByDescending { get; }

        int? Skip { get; }
        int? Take { get; }

        bool IsPagingEnabled { get; }
    }

}
