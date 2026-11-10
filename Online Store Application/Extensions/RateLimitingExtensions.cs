using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Application.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Extensions;

/// <summary>نام سیاست‌های محدودیت نرخ که روی endpointها با EnableRateLimiting استفاده می‌شوند.</summary>
public static class RateLimitPolicies
{
    public const string Login = "LoginLimiter";
    public const string Register = "RegisterLimiter";
    public const string Refresh = "RefreshLimiter";
    public const string Checkout = "CheckoutLimiter";
    public const string PaymentInitiate = "PaymentInitiateLimiter";
}

/// <summary>F-03: سقف‌ها باید مثبت باشند؛ در غیر این صورت راه‌اندازی شکست می‌خورد (ValidateOnStart).</summary>
public sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var errors = new List<string>();

        Check(nameof(options.Login), options.Login, errors);
        Check(nameof(options.Register), options.Register, errors);
        Check(nameof(options.Refresh), options.Refresh, errors);
        Check(nameof(options.Checkout), options.Checkout, errors);
        Check(nameof(options.PaymentInitiate), options.PaymentInitiate, errors);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Check(string policy, FixedWindowLimitOptions? limit, List<string> errors)
    {
        if (limit is null)
        {
            errors.Add($"RateLimiting:{policy} تنظیم نشده است.");
            return;
        }

        if (limit.PermitLimit < 1)
            errors.Add($"RateLimiting:{policy}:PermitLimit باید حداقل ۱ باشد.");

        if (limit.WindowSeconds is < 1 or > 86400)
            errors.Add($"RateLimiting:{policy}:WindowSeconds باید بین ۱ و ۸۶۴۰۰ ثانیه باشد.");
    }
}

public static class RateLimitingExtensions
{
    private const string ErrorCode = "RATE_LIMIT_EXCEEDED";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// F-03: هر سیاست یک limiter پنجرهٔ ثابت به‌ازای هر کلاینت است، نه یک سطل برای کل سامانه.
    /// endpointهای ناشناس (ورود، ثبت‌نام، تازه‌سازی) با IP کلاینت و endpointهای احرازشده
    /// (checkout، شروع پرداخت) با شناسهٔ کاربر کلید می‌خورند. پاسخ رد شده 429 با Retry-After است.
    /// UseRateLimiter باید پس از UseForwardedHeaders و UseAuthentication بیاید.
    /// </summary>
    public static IServiceCollection AddPartitionedRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services.AddRateLimiter(_ => { });

        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((options, limitsAccessor) =>
            {
                var limits = limitsAccessor.Value;

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, cancellationToken) =>
                    WriteRejectionAsync(context, limits, cancellationToken);

                options.AddPolicy(RateLimitPolicies.Login, ctx => ByClientIp(ctx, limits.Login));
                options.AddPolicy(RateLimitPolicies.Register, ctx => ByClientIp(ctx, limits.Register));
                options.AddPolicy(RateLimitPolicies.Refresh, ctx => ByClientIp(ctx, limits.Refresh));
                options.AddPolicy(RateLimitPolicies.Checkout, ctx => ByUserOrClientIp(ctx, limits.Checkout));
                options.AddPolicy(RateLimitPolicies.PaymentInitiate, ctx => ByUserOrClientIp(ctx, limits.PaymentInitiate));
            });

        return services;
    }

    private static RateLimitPartition<string> ByClientIp(HttpContext context, FixedWindowLimitOptions limit) =>
        FixedWindow("ip:" + (context.GetClientIpAddress() ?? "unknown"), limit);

    private static RateLimitPartition<string> ByUserOrClientIp(HttpContext context, FixedWindowLimitOptions limit)
    {
        var userId = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        return string.IsNullOrEmpty(userId)
            ? ByClientIp(context, limit)
            : FixedWindow("user:" + userId, limit);
    }

    private static RateLimitPartition<string> FixedWindow(string key, FixedWindowLimitOptions limit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });

    /// <summary>بدنهٔ استاندارد خطای پروژه (ProblemDetails با code و traceId) به‌همراه هدر Retry-After.</summary>
    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext context,
        RateLimitingOptions limits,
        CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter)
            ? leaseRetryAfter
            : TimeSpan.FromSeconds(WindowOf(policy, limits));
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting")
            .LogWarning(
                "Rate limit exceeded: policy {Policy}, path {Path}, client IP {ClientIp}, user {UserId}",
                policy,
                http.Request.Path.Value,
                http.GetClientIpAddress(),
                http.User.FindFirstValue(ClaimTypes.NameIdentifier));

        if (http.Response.HasStarted)
            return;

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.ContentType = "application/problem+json";
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Type = $"https://api.yourstore.com/errors/{ErrorCode.ToLowerInvariant()}",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = $"تعداد درخواست‌های شما بیش از حد مجاز است. لطفاً {retryAfterSeconds} ثانیهٔ دیگر دوباره تلاش کنید.",
            Instance = http.Request.Path
        };
        problem.Extensions["code"] = ErrorCode;
        problem.Extensions["traceId"] = http.TraceIdentifier;
        problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;

        await http.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions), cancellationToken);
    }

    private static int WindowOf(string? policy, RateLimitingOptions limits) => policy switch
    {
        RateLimitPolicies.Login => limits.Login.WindowSeconds,
        RateLimitPolicies.Register => limits.Register.WindowSeconds,
        RateLimitPolicies.Refresh => limits.Refresh.WindowSeconds,
        RateLimitPolicies.Checkout => limits.Checkout.WindowSeconds,
        RateLimitPolicies.PaymentInitiate => limits.PaymentInitiate.WindowSeconds,
        _ => 60
    };
}
