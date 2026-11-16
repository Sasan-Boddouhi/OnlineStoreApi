using Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Online_Store_Application.Swagger;

/// <summary>
/// Batch 67 (API-05): پاسخ‌های خطای پیش‌فرض (400، 401، 403، 404، 409، 422، 429، 500) با یک schema مشترک
/// ProblemDetails به هر عملیات اضافه می‌شوند. 401/403 فقط برای عملیات نیازمند احراز هویت می‌آیند.
/// پاسخی که کنترلر خودش اعلام کرده بازنویسی نمی‌شود.
/// </summary>
public sealed class DefaultErrorResponsesFilter : IOperationFilter
{
    private const string ProblemJson = "application/problem+json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var problem = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        var validation = context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository);

        var statuses = new List<int> { 400 };
        if (RequiresAuthorization(context))
        {
            statuses.Add(401);
            statuses.Add(403);
        }
        statuses.AddRange(new[] { 404, 409, 422, 429, 500 });

        foreach (var status in statuses)
        {
            var key = status.ToString();
            if (operation.Responses.ContainsKey(key))
                continue;

            var schema = status is 400 or 422 ? validation : problem;
            var codes = ErrorCodes.All.Where(e => e.Status == status).Select(e => e.Code).ToList();

            var response = new OpenApiResponse
            {
                Description = Describe(status),
                Content = { [ProblemJson] = new OpenApiMediaType { Schema = schema } }
            };

            // x-error-codes: کدهای رجیستری که با این وضعیت می‌آیند (منبع: ErrorCodes.All).
            if (codes.Count > 0)
            {
                var array = new OpenApiArray();
                array.AddRange(codes.Select(c => (IOpenApiAny)new OpenApiString(c)));
                response.Extensions["x-error-codes"] = array;
            }

            operation.Responses[key] = response;
        }
    }

    private static bool RequiresAuthorization(OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
            return false;
        if (metadata.OfType<IAuthorizeData>().Any())
            return true;

        var controller = (context.ApiDescription.ActionDescriptor as ControllerActionDescriptor)?.ControllerTypeInfo;
        return controller is not null && controller.GetCustomAttributes(true).OfType<IAuthorizeData>().Any();
    }

    private static string Describe(int status) => status switch
    {
        400 => "Bad request or invalid query parameters.",
        401 => "Not authenticated.",
        403 => "Not allowed.",
        404 => "Not found.",
        409 => "Conflict with the current state.",
        422 => "Validation failed.",
        429 => "Rate limit exceeded; see the Retry-After header.",
        _ => "Unexpected server error."
    };
}
