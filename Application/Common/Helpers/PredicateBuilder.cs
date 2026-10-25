using Application.Common.Specifications;
using System.Linq.Expressions;

namespace Application.Common.Helpers;

public static class PredicateBuilder
{
    public static Expression<Func<T, bool>> True<T>() => _ => true;

    public static Expression<Func<T, bool>> False<T>() => _ => false;

    public static Expression<Func<T, bool>> And<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
        => Combine(left, right, Expression.AndAlso);

    public static Expression<Func<T, bool>> Or<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
        => Combine(left, right, Expression.OrElse);

    public static Expression<Func<T, bool>> Not<T>(
        this Expression<Func<T, bool>> expression)
    {
        var parameter = expression.Parameters[0];
        var body = Expression.Not(expression.Body);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> combiner)
    {
        var parameter = Expression.Parameter(typeof(T), "x");

        var leftBody = new ReplaceExpressionVisitor(
            left.Parameters[0],
            parameter).Visit(left.Body)!;

        var rightBody = new ReplaceExpressionVisitor(
            right.Parameters[0],
            parameter).Visit(right.Body)!;

        var body = combiner(leftBody, rightBody);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}