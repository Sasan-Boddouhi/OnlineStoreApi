using System.Linq.Expressions;
using System.Reflection;

namespace Application.Common.Helpers
{
    public static class ExpressionBuilder
    {
        /// <summary>
        /// عبارت دسترسی به ویژگی (مسیر نقطه‌دار، مثل <c>subcategory.subcategoryname</c>) را می‌سازد و کش می‌کند.
        /// Batch 60 (F-24): هر بخش مسیر بدون حساسیت به حروف بزرگ و کوچک به نام واقعی ویژگی تبدیل می‌شود
        /// و کلید کش همین نام واقعی است؛ پس <c>name</c> و <c>NAME</c> یک ورودی مشترک دارند.
        /// مسیر ناموجود <see cref="ArgumentException"/> می‌دهد و هیچ‌چیز کش نمی‌شود.
        /// </summary>
        public static LambdaExpression BuildPropertyLambdaCached<T>(string propertyPath)
        {
            var canonical = CanonicalPath(typeof(T), propertyPath);
            return ExpressionCache<T>.GetOrAdd(canonical, path =>
            {
                var parameter = Expression.Parameter(typeof(T), "x");
                var property = GetNestedProperty(parameter, path);
                var delegateType = typeof(Func<,>).MakeGenericType(typeof(T), property.Type);
                return Expression.Lambda(delegateType, property, parameter);
            });
        }

        /// <summary>
        /// مسیر را به نام‌های واقعی ویژگی‌ها برمی‌گرداند (مثلاً <c>subcategory.subcategoryname</c> ←
        /// <c>Subcategory.SubcategoryName</c>)؛ اگر بخشی وجود نداشته باشد <see cref="ArgumentException"/>.
        /// </summary>
        public static string CanonicalPath(Type type, string propertyPath)
        {
            if (string.IsNullOrWhiteSpace(propertyPath))
                throw new ArgumentException("مسیر ویژگی خالی است.", nameof(propertyPath));

            var segments = propertyPath.Split('.');
            var current = type;
            for (var i = 0; i < segments.Length; i++)
            {
                var property = FindProperty(current, segments[i])
                    ?? throw new ArgumentException($"فیلد «{propertyPath}» وجود ندارد.", nameof(propertyPath));
                segments[i] = property.Name;
                current = property.PropertyType;
            }
            return string.Join('.', segments);
        }

        private static PropertyInfo? FindProperty(Type type, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            // نام دقیق مقدم است؛ سپس بدون حساسیت به حروف. اگر چند ویژگی فقط در حروف فرق کنند، ابهام ← ناموجود.
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .ToList();

            var exact = properties.Where(p => p.Name == name).ToList();
            if (exact.Count >= 1)
                return exact[0];

            var matches = properties
                .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
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
