using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace Application.Common.Helpers;

public class ReplaceExpressionVisitor : ExpressionVisitor
{
    private readonly Expression _oldValue;
    private readonly Expression _newValue;
    private readonly bool _useStructuralComparison;

    public ReplaceExpressionVisitor(Expression oldValue, Expression newValue, bool useStructuralComparison = false)
    {
        _oldValue = oldValue;
        _newValue = newValue;
        _useStructuralComparison = useStructuralComparison;
    }

    public override Expression? Visit(Expression? node)
    {
        if (node == null) return null;

        bool shouldReplace = _useStructuralComparison   
            ? ExpressionEqualityComparer.Instance.Equals(node, _oldValue)
            : node == _oldValue;

        if (shouldReplace)
            return _newValue;

        return base.Visit(node);
    }
}
