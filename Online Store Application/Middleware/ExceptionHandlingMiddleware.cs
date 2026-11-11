using System.Net;
using System.Text.Json;
using Application.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Online_Store_Application.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning(ex, "Application exception: {Code} - {Message}", ex.Code, ex.Message);

            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Type = $"https://api.yourstore.com/errors/{ex.Code.ToLowerInvariant()}",
                Title = GetTitleForStatusCode(ex.StatusCode),
                Status = ex.StatusCode,
                Detail = ex.Message,
                Instance = context.Request.Path
            };
            problem.Extensions["code"] = ex.Code;
            problem.Extensions["traceId"] = context.TraceIdentifier;

            if (ex is LockedOutException lockedOut)
            {
                problem.Extensions["lockoutEnd"] = lockedOut.LockoutEnd;
                problem.Extensions["remainingSeconds"] = lockedOut.RemainingSeconds;
            }

            // Batch 5 (F-07): 503 قابل تکرار؛ کلاینت پس از Retry-After ثانیه دوباره تلاش می‌کند.
            if (ex is ServiceUnavailableException unavailable && !context.Response.HasStarted)
            {
                context.Response.Headers.RetryAfter = unavailable.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                problem.Extensions["retryAfterSeconds"] = unavailable.RetryAfterSeconds;
            }

            if (!context.Response.HasStarted)
            {
                await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
            }
        }
        catch (BusinessRuleViolationException ex)
        {
            // Batch 7 (F-16): نقض قاعدهٔ دامنه (مثلاً لغو سفارش ارسال‌شده) تعارض وضعیت است، نه خطای سرور.
            _logger.LogWarning(ex, "Business rule violation: {Code} - {Message}", ex.Code, ex.Message);

            await WriteConflictAsync(context, ex.Code, "عملیات درخواستی در وضعیت فعلی مجاز نیست.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict");

            await WriteConflictAsync(context, "CONCURRENCY_CONFLICT", "تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.");
        }
        catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueViolation(ex))
        {
            // جزئیات دیتابیس (نام جدول/ایندکس) عمداً در پاسخ نمی‌آید.
            _logger.LogWarning(ex, "Unique constraint violation");

            await WriteConflictAsync(context, "DUPLICATE_RECORD", "رکوردی با همین مشخصات قبلاً ثبت شده است.");
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed");

            if (context.Response.HasStarted)
                return;

            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            context.Response.ContentType = "application/problem+json";

            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            var problem = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Validation Error",
                Detail = "One or more validation errors occurred.",
                Instance = context.Request.Path
            };
            problem.Extensions["code"] = "VALIDATION_ERROR";
            problem.Extensions["traceId"] = context.TraceIdentifier;

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Request was cancelled by the client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

            if (context.Response.HasStarted)
                return;

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred. Please try again later.",
                Instance = context.Request.Path
            };
            problem.Extensions["traceId"] = context.TraceIdentifier;

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        }
    }

    private static async Task WriteConflictAsync(HttpContext context, string code, string detail)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = $"https://api.yourstore.com/errors/{code.ToLowerInvariant()}",
            Title = GetTitleForStatusCode(StatusCodes.Status409Conflict),
            Status = StatusCodes.Status409Conflict,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private static string GetTitleForStatusCode(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Error"
    };
}