using Application.Options;

namespace Online_Store_Application.Extensions;

/// <summary>
/// F-04: سیاست‌های نام‌دار کش خروجی.
/// کش خروجی opt-in است: فقط endpointی کش می‌شود که یکی از این سیاست‌ها را صراحتاً روی خود داشته باشد.
/// هرگز روی سبد، پرداخت، سفارش، موجودی، احراز هویت یا هر چیزی که به کاربر/نشست جاری وابسته است نگذارید.
/// </summary>
public static class OutputCachePolicies
{
    /// <summary>داده‌های مرجع عمومی و فقط‌خواندنی: دسته‌بندی، برند، استان/شهر، روش ارسال.</summary>
    public const string ReferenceData = "ReferenceData";

    /// <summary>جزئیات عمومی یک محصول. کوتاه، چون قیمت پایه در آن دیده می‌شود.</summary>
    public const string ProductDetail = "ProductDetail";

    public static readonly TimeSpan ReferenceDataDuration = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan ProductDetailDuration = TimeSpan.FromSeconds(30);
}

public static class OutputCacheExtensions
{
    /// <summary>
    /// کش خروجی روشن است؟ مقدار صریح <see cref="OutputCachingOptions.Enabled"/> مقدم است؛
    /// در غیر این صورت در Testing خاموش و در بقیهٔ محیط‌ها روشن است.
    /// </summary>
    public static bool IsOutputCacheEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(OutputCachingOptions.SectionName).Get<OutputCachingOptions>()
            ?? new OutputCachingOptions();

        return options.Enabled ?? !environment.IsEnvironment("Testing");
    }

    /// <summary>
    /// ثبت کش خروجی بدون سیاست پایه. store در Testing درون‌حافظه‌ای و در بقیهٔ محیط‌ها Redis است.
    /// </summary>
    public static IServiceCollection AddApplicationOutputCache(
        this IServiceCollection services,
        IHostEnvironment environment,
        RedisOptions redisOptions)
    {
        services.AddOutputCache(options =>
        {
            // F-04: عمداً AddBasePolicy نداریم. سیاست پایه هر GET بی‌توکن را فقط بر اساس URL کش می‌کرد
            // و هدر X-Cart-Session را نمی‌دید؛ مهمان دوم سبد مهمان اول را می‌گرفت.

            // این endpointها پارامتر query ندارند (نسخه در مسیر است)، پس query string از کلید حذف می‌شود
            // تا درخواست‌های بی‌نام با query تصادفی نتوانند store را پر کنند.
            options.AddPolicy(OutputCachePolicies.ReferenceData, policy => policy
                .Expire(OutputCachePolicies.ReferenceDataDuration)
                .SetVaryByQuery(Array.Empty<string>()));

            options.AddPolicy(OutputCachePolicies.ProductDetail, policy => policy
                .Expire(OutputCachePolicies.ProductDetailDuration)
                .SetVaryByQuery(Array.Empty<string>()));
        });

        if (!environment.IsEnvironment("Testing"))
        {
            services.AddStackExchangeRedisOutputCache(options =>
            {
                options.Configuration = redisOptions.Configuration;
                options.InstanceName = redisOptions.InstanceName;
            });
        }

        return services;
    }
}
