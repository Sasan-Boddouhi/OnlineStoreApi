using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Application.Common.Helpers
{
    public static class ExpressionCache<T>
    {
        private static readonly ConcurrentDictionary<string, LambdaExpression> _cache = new();

        public static LambdaExpression GetOrAdd(string key, Func<string, LambdaExpression> factory)
            => _cache.GetOrAdd(key, factory);
    }
}