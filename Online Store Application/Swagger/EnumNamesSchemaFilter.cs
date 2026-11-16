using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Online_Store_Application.Swagger;

/// <summary>
/// Batch 67 (API-05): enumها در JSON فعلی عدد هستند (بدون StringEnumConverter) و تغییر قالب سیمی شکستن فرانت است.
/// پس schema عددی می‌ماند و نام‌ها در <c>x-enum-varnames</c> می‌آیند تا مولد کلاینت نام ثابت بسازد.
/// </summary>
public sealed class EnumNamesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
            return;

        var names = new OpenApiArray();
        foreach (var value in Enum.GetValues(context.Type))
            names.Add(new OpenApiString(Enum.GetName(context.Type, value)));
        schema.Extensions["x-enum-varnames"] = names;
    }
}
