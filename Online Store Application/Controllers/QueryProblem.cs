using Application.Common.Queries;
using Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Online_Store_Application.Extensions;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Batch 53 (API-04): بدنهٔ یکسان 400 برای خطاهای نحو/مقدار فیلتر و مرتب‌سازی با کد <c>INVALID_QUERY</c>.
/// </summary>
internal static class QueryProblem
{
    public static BadRequestObjectResult Create(HttpContext context, string title, IReadOnlyCollection<ParseError> errors)
    {
        var problem = ProblemDetailsWriter.Create(
            context, ErrorCodes.InvalidQuery.Code, ErrorCodes.InvalidQuery.Status,
            string.Join("; ", errors.Select(e => e.Message)), title);
        problem.Extensions["errors"] = errors.Select(e => new { e.Code, e.Message, e.Target }).ToArray();
        return new BadRequestObjectResult(problem);
    }
}
