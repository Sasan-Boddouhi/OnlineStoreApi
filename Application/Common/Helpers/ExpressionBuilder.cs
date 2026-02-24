using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Helpers
{
    public static class ExpressionBuilder
    {
        public static LambdaExpression BuildPropertyLambdaCached<T>(string propertyPath)
        {
            return ExpressionCache<T>.GetOrAdd(propertyPath, path =>
            {
                var parameter = Expression.Parameter(typeof(T), "x");
                var property = GetNestedProperty(parameter, path);
                var delegateType = typeof(Func<,>).MakeGenericType(typeof(T), property.Type);
                return Expression.Lambda(delegateType, property, parameter);
            });
        }

        private static MemberExpression GetNestedProperty(Expression parameter, string propertyPath)
        {
            var properties = propertyPath.Split('.');
            Expression current = parameter;
            foreach (var prop in properties)
                current = Expression.Property(current, prop);
            return (MemberExpression)current;
        }
    }
}
