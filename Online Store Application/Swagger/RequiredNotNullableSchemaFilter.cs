using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Online_Store_Application.Swagger;

/// <summary>
/// Batch 67 (API-05): هر عضوی که nullable نیست در schema «required» می‌شود تا مولد کلاینت
/// فیلدهای همیشه‌حاضر را اختیاری نکند. عضو nullable (<c>string?</c>، <c>int?</c>) اختیاری می‌ماند.
/// </summary>
public sealed class RequiredNotNullableSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null || schema.Properties.Count == 0)
            return;

        foreach (var (name, property) in schema.Properties)
        {
            if (property.Nullable || property.WriteOnly)
                continue;
            schema.Required.Add(name);
        }
    }
}
