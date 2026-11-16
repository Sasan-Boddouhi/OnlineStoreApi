using System.Text.Json;
using Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Extensions;

/// <summary>
/// Batch 53 (API-04/06): نقطهٔ یگانهٔ ساخت و نوشتن بدنهٔ خطا (ProblemDetails با <c>code</c> و <c>traceId</c>).
/// <c>detail</c> متن فارسی نمایشی است و <c>code</c> کلید ماشینی.
/// </summary>
public static class ProblemDetailsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static ProblemDetails Create(HttpContext context, string code, int status, string? detail, string? title = null)
    {
        var problem = new ProblemDetails
        {
            Type = ErrorCodes.TypeUri(code),
            Title = title ?? TitleFor(status),
            Status = status,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }

    public static ProblemDetails Create(HttpContext context, ErrorCodeInfo info, string? detail = null) =>
        Create(context, info.Code, info.Status, detail ?? info.Message);

    public static Task WriteAsync(HttpContext context, ErrorCodeInfo info, string? detail = null) =>
        WriteAsync(context, Create(context, info, detail));

    public static async Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    public static string TitleFor(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        415 => "Unsupported Media Type",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Error"
    };

    /// <summary>
    /// دلیل شکست احراز هویت JWT را به کد رجیستری نگاشت می‌کند. شکست‌های ثبت‌شده در
    /// <c>OnTokenValidated</c> با <see cref="JwtFailure"/> علامت‌گذاری می‌شوند.
    /// </summary>
    public static ErrorCodeInfo ClassifyJwtFailure(Exception? failure) => failure switch
    {
        null => ErrorCodes.Unauthenticated,
        JwtFailure f => f.Info,
        Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException => ErrorCodes.TokenExpired,
        _ => ErrorCodes.TokenInvalid
    };
}

/// <summary>شکست احراز هویت JWT با کد مشخص رجیستری.</summary>
public sealed class JwtFailure : Exception
{
    public ErrorCodeInfo Info { get; }

    public JwtFailure(ErrorCodeInfo info) : base(info.Code) => Info = info;
}
