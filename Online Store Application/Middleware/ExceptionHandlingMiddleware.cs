using System.Net;
using System.Text.Json;
using Application.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Middleware;

public sealed class ExceptionHandlingMiddleware
{
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

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed");

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
            problem.Extensions["traceId"] = context.TraceIdentifier;

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

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

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
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
        _ => "Error"
    };
}