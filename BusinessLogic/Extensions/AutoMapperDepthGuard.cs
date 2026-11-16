using System.Reflection;
using AutoMapper;
using AutoMapper.Internal;

namespace BusinessLogic.Extensions;

/// <summary>
/// Batch 40 (ARC-05، GHSA-rvv3-g6hj-g44x / CVE-2026-32933): AutoMapper 14 برای نوع‌های خودارجاع سقف عمق ندارد؛
/// نگاشت یک گراف خیلی عمیق با StackOverflowException کل فرایند را می‌کشد (قابل catch نیست).
/// نسخه‌های اصلاح‌شده (15.1.1 / 16.1.1، با مجوز تجاری) برای نگاشت‌های چرخه‌دار پیش‌فرض MaxDepth = 64 می‌گذارند؛
/// این کلاس همان رفتار را روی نسخهٔ 14 بازسازی می‌کند: هر نگاشتی که نوع مبدأ و مقصدش هر دو (مستقیم یا از راه
/// مجموعه‌ها) به خودشان برمی‌گردند و MaxDepth صریح ندارد، سقف <see cref="MaxDepth"/> می‌گیرد.
/// گراف عمیق‌تر از سقف کوتاه می‌شود (null)، نه خطا. نگاشت‌های بدون چرخه دست نمی‌خورند.
/// </summary>
public static class AutoMapperDepthGuard
{
    /// <summary>همان مقدار نسخه‌های اصلاح‌شده و پیش‌فرض System.Text.Json / Newtonsoft.Json.</summary>
    public const int MaxDepth = 64;

    /// <summary>روی پیکربندی سراسری AutoMapper سوار می‌شود و به نگاشت‌های همهٔ Profileها اعمال می‌شود.</summary>
    public static void Apply(IMapperConfigurationExpression configuration)
    {
        configuration.Internal().ForAllMaps((typeMap, map) =>
        {
            // بازگشت بی‌پایان فقط وقتی ممکن است که هم گراف مبدأ و هم مقصد به خودشان برگردند؛
            // نگاشت DTO تخت → entity یا entity → DTO تخت عمق محدود دارد و دست نمی‌خورد.
            if (typeMap.MaxDepth == 0
                && IsSelfReferential(typeMap.SourceType)
                && IsSelfReferential(typeMap.DestinationType))
            {
                map.MaxDepth(MaxDepth);
            }
        });
    }

    /// <summary>آیا از ویژگی‌های عمومی این نوع (و عناصر مجموعه‌ها) دوباره به خود نوع می‌رسیم؟</summary>
    public static bool IsSelfReferential(Type type)
    {
        var visited = new HashSet<Type>();
        var pending = new Stack<Type>(MemberTypes(type));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current == type)
                return true;

            if (!visited.Add(current))
                continue;

            foreach (var next in MemberTypes(current))
                pending.Push(next);
        }

        return false;
    }

    private static IEnumerable<Type> MemberTypes(Type type)
    {
        if (IsLeaf(type))
            yield break;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var candidate in Unwrap(property.PropertyType))
            {
                if (!IsLeaf(candidate))
                    yield return candidate;
            }
        }
    }

    /// <summary>Nullable، آرایه و نوع‌های عام (List، Dictionary، ...) را به آرگومان‌هایشان باز می‌کند.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.IsArray)
            return Unwrap(type.GetElementType()!);

        if (type.IsGenericType)
            return type.GetGenericArguments().SelectMany(Unwrap).Append(type);

        return new[] { type };
    }

    /// <summary>
    /// نوع‌های چارچوب (System.*، Microsoft.*) — از جمله خود List و Dictionary پس از باز شدن آرگومان‌ها —
    /// و نوع‌های ساده پیمایش نمی‌شوند؛ فقط نوع‌های خود برنامه.
    /// </summary>
    private static bool IsLeaf(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type.IsPointer
        || type.IsGenericParameter
        || type.Namespace is null
        || type.Namespace.StartsWith("System", StringComparison.Ordinal)
        || type.Namespace.StartsWith("Microsoft", StringComparison.Ordinal);
}
