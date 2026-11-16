using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Online_Store_Application.Swagger;

/// <summary>
/// Batch 67 (API-05): <c>code</c> و <c>traceId</c> را <c>ProblemDetailsWriter</c> در Extensions می‌نویسد و
/// Swashbuckle آن‌ها را نمی‌بیند؛ این فیلتر آن‌ها را به schema مشترک ProblemDetails / ValidationProblemDetails اضافه می‌کند.
/// </summary>
public sealed class ProblemSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ProblemDetails) && context.Type != typeof(ValidationProblemDetails))
            return;

        schema.Properties["code"] = new OpenApiSchema
        {
            Type = "string",
            Description = "Machine-readable error code from the registry (openapi/error-codes.json)."
        };
        schema.Properties["traceId"] = new OpenApiSchema
        {
            Type = "string",
            Description = "Request trace identifier."
        };
        schema.Required.Add("code");
        schema.Required.Add("traceId");
    }
}
