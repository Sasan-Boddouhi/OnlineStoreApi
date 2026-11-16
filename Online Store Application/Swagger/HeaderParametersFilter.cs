using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Online_Store_Application.Swagger;

/// <summary>
/// Batch 67 (API-05): هدرهای سفارشی در امضای اکشن‌ها نیستند و خودکار مستند نمی‌شوند.
/// <c>X-Cart-Session</c> روی عملیات سبد و checkout، <c>Idempotency-Key</c> روی آغاز پرداخت، و
/// <c>X-Client-Platform</c> / <c>X-App-Version</c> (اختیاری) روی همهٔ عملیات اضافه می‌شوند.
/// </summary>
public sealed class HeaderParametersFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var controller = (context.ApiDescription.ActionDescriptor as ControllerActionDescriptor)?.ControllerName;

        if (controller == "Carts")
            Add(operation, "X-Cart-Session",
                "Guest cart identifier. Send it for guests; signed-in users are identified by the token.");

        if (controller == "Payments" && context.ApiDescription.RelativePath is { } path
            && path.EndsWith("payments/initiate", StringComparison.OrdinalIgnoreCase))
            Add(operation, "Idempotency-Key",
                "Unique key per payment attempt; a repeated request with the same key returns the same payment.");

        Add(operation, "X-Client-Platform", "Optional client platform name, for example web, android or ios.");
        Add(operation, "X-App-Version", "Optional client application version.");
    }

    private static void Add(OpenApiOperation operation, string name, string description)
    {
        if (operation.Parameters.Any(p => p.In == ParameterLocation.Header
                                          && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = false,
            Description = description,
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
