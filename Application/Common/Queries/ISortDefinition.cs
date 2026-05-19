using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Queries
{
    public interface ISortDefinition<TEntity>
    {
        LambdaExpression KeySelector { get; }

        bool Descending { get; }
    }
}