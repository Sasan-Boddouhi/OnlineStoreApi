using Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Extensions;

/// <summary>
/// Batch 53 (API-04): ProblemDetailsهای تولیدشدهٔ خود MVC (مثلاً 415 از سد content-type یا <c>NotFound()</c> بدون بدنه)
/// نیز <c>code</c>، <c>traceId</c> و <c>type</c> واقعی می‌گیرند؛ فقط وقتی وضعیت با ورودی رجیستری یکی باشد.
/// </summary>
public sealed class CodedProblemDetailsFactory : ProblemDetailsFactory
{
    private readonly ApiBehaviorOptions _options;

    public CodedProblemDetailsFactory(IOptions<ApiBehaviorOptions> options) => _options = options.Value;

    public override ProblemDetails CreateProblemDetails(
        HttpContext httpContext, int? statusCode = null, string? title = null,
        string? type = null, string? detail = null, string? instance = null)
    {
        var status = statusCode ?? StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };
        Apply(httpContext, problem, status);
        return problem;
    }

    public override ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext, Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelStateDictionary,
        int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null)
    {
        var status = statusCode ?? StatusCodes.Status400BadRequest;
        var problem = new ValidationProblemDetails(modelStateDictionary)
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };
        Apply(httpContext, problem, status);
        return problem;
    }

    private void Apply(HttpContext httpContext, ProblemDetails problem, int status)
    {
        if (_options.ClientErrorMapping.TryGetValue(status, out var data))
            problem.Title ??= data.Title;

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        var info = ErrorCodes.ForStatus(status);
        if (info is null)
            return;

        problem.Type = ErrorCodes.TypeUri(info.Code);
        problem.Detail ??= info.Message;
        problem.Extensions["code"] = info.Code;
    }
}
