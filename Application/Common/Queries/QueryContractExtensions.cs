using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Application.Common.Queries;

public static class QueryContractExtensions
{
    public static (int Skip, int Take, int Page, int Size) ToPaging<TEntity>(
        this QueryContract<TEntity> query)
        where TEntity : class
    {
        var page = query.Page ?? 1;
        var size = query.Size ?? 20;

        return (
            (page - 1) * size,
            size,
            page,
            size
        );
    }

    /// <summary>
    /// F-23: کلید کش یک query؛ <c>{prefix}:q:{sha256}</c> با طول ثابت.
    /// پیش از این متن خام فیلتر داخل کلید می‌رفت (طول نامحدود)، و مرتب‌سازی فقط با نام نوعش ثبت می‌شد،
    /// پس <c>sort=name</c> و <c>sort=price</c> یک کلید مشترک داشتند.
    /// </summary>
    public static string ToCacheKey<TEntity>(this QueryContract<TEntity> query, string prefix) where TEntity : class
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(query)));
        return $"{prefix}:q:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>شکل متنی یکتا و پایدار از query که هش می‌شود.</summary>
    private static string Normalize<TEntity>(QueryContract<TEntity> query) where TEntity : class
    {
        var p = query.Page ?? 1;
        var s = query.Size ?? 20;
        var skip = query.Skip?.ToString() ?? "0";
        var take = query.Take?.ToString() ?? s.ToString();
        var filter = query.Filter != null ? ToStableString(query.Filter) : "none";
        var sorts = query.Sorts != null && query.Sorts.Count > 0
            ? string.Join(",", query.Sorts.Select(x => $"{(x.Descending ? "desc" : "asc")}:{ToStableString(x.KeySelector)}"))
            : "none";

        return $"p{p}|s{s}|sk{skip}|tk{take}|f{filter}|o{sorts}";
    }

    /// <summary>
    /// متن عبارت، با ثابت‌های رشته‌ای JSON-escape‌شده؛ <see cref="Expression.ToString"/> کوتیشن داخل مقدار را
    /// escape نمی‌کند و دو فیلتر متفاوت می‌توانستند یک متن داشته باشند.
    /// </summary>
    private static string ToStableString(Expression expression)
        => StringConstantEscaper.Instance.Visit(expression)!.ToString();

    private sealed class StringConstantEscaper : ExpressionVisitor
    {
        public static readonly StringConstantEscaper Instance = new();

        protected override Expression VisitConstant(ConstantExpression node)
            => node.Value is string value
                ? Expression.Constant(JsonSerializer.Serialize(value), typeof(string))
                : node;
    }
}
