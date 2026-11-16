using System.Text.Json;
using Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Online_Store_Application.Middleware;

/// <summary>
/// N14: بدنهٔ JSON عمیق‌تر از <see cref="MaxDepth"/> پیش از رسیدن به MVC با 422 و ProblemDetails رد می‌شود.
/// Newtonsoft هنگام عبور از سقف عمق در مسیر خطای MVC با InvalidOperationException می‌شکند (500)؛
/// این پیش‌بررسی روی بدنهٔ بافرشده جریانی است و JSON خراب را (برای MVC) دست‌نخورده رد می‌کند.
/// </summary>
public sealed class JsonDepthGuardMiddleware
{
    /// <summary>هم‌سقف با <c>SerializerSettings.MaxDepth</c> در Program.cs.</summary>
    public const int MaxDepth = 64;

    private static readonly JsonSerializerOptions ProblemJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RequestDelegate _next;

    public JsonDepthGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (HasJsonBody(request))
        {
            request.EnableBuffering();
            var tooDeep = await ExceedsMaxDepthAsync(request.Body, context.RequestAborted);
            request.Body.Position = 0;

            if (tooDeep)
            {
                await WriteRejectionAsync(context);
                return;
            }
        }

        await _next(context);
    }

    private static bool HasJsonBody(HttpRequest request) =>
        (request.ContentLength is null or > 0)
        && request.ContentType is { } contentType
        && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> ExceedsMaxDepthAsync(Stream body, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(body, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
        using var reader = new JsonTextReader(streamReader) { MaxDepth = null };
        try
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                // Depth یک StartObject/StartArray شمارندهٔ صفرپایهٔ همان سطح است؛ سطح 65 یعنی Depth = 64.
                if ((reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.StartArray)
                    && reader.Depth >= MaxDepth)
                    return true;
            }
        }
        catch (JsonReaderException)
        {
            // JSON خراب است نه عمیق؛ همان پاسخ همیشگی MVC برای بدنهٔ نامعتبر.
        }

        return false;
    }

    private static async Task WriteRejectionAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = ErrorCodes.TypeUri(ErrorCodes.ValidationError.Code),
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Validation Error",
            Detail = ErrorCodes.ValidationError.Message,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = ErrorCodes.ValidationError.Code;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(problem, ProblemJson));
    }
}
