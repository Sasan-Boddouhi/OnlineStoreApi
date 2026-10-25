// Application/Common/Queries/Internal/LambdaSortDefinition.cs
using System.Linq.Expressions;

namespace Application.Common.Queries.Internal;

internal sealed class LambdaSortDefinition<TEntity> : ISortDefinition<TEntity>
{
    private readonly LambdaExpression _keySelector;
    private readonly bool _descending;

    public LambdaSortDefinition(LambdaExpression keySelector, bool descending)
    {
        _keySelector = keySelector;
        _descending = descending;
    }

    LambdaExpression ISortDefinition<TEntity>.KeySelector => _keySelector;
    public bool Descending => _descending;
}