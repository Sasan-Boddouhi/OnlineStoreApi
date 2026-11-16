using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Application.Common.Helpers
{
    /// <summary>
    /// کش عبارت‌های دسترسی به ویژگی برای نوع <typeparamref name="T"/>.
    /// Batch 60 (F-24): اندازهٔ کش محدود است. کلیدها از ورودی کاربر (فیلد فیلتر) می‌آیند؛ پیش از این
    /// دیکشنری بی‌سقف بود و هر املای تازه یک ورودی دائمی می‌ساخت. وقتی کش پر است، عبارت ساخته
    /// و برگردانده می‌شود ولی ذخیره نمی‌شود (هیچ‌وقت خطا نمی‌دهد و از سقف بزرگ‌تر نمی‌شود).
    /// </summary>
    public static class ExpressionCache<T>
    {
        /// <summary>بیشترین تعداد ورودی کش برای هر نوع.</summary>
        public const int MaxEntries = 256;

        private static readonly ConcurrentDictionary<string, LambdaExpression> _cache = new(StringComparer.Ordinal);

        /// <summary>تعداد ورودی‌های فعلی (برای آزمون و پایش).</summary>
        public static int Count => _cache.Count;

        public static LambdaExpression GetOrAdd(string key, Func<string, LambdaExpression> factory)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var built = factory(key);

            lock (_cache)
            {
                if (_cache.TryGetValue(key, out cached))
                    return cached;

                if (_cache.Count >= MaxEntries)
                    return built;

                _cache[key] = built;
                return built;
            }
        }
    }
}
