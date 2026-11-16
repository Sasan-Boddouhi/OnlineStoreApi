using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Online_Store_Application.Swagger;

public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        // Batch 67 (API-05): شناسهٔ عملیات پایدار {Controller}_{Action}؛ nullability از روی C# خوانده می‌شود.
        options.CustomOperationIds(api =>
            api.ActionDescriptor.RouteValues.TryGetValue("controller", out var c) &&
            api.ActionDescriptor.RouteValues.TryGetValue("action", out var a)
                ? $"{c}_{a}"
                : null);
        options.SupportNonNullableReferenceTypes();
        options.SchemaFilter<ProblemSchemaFilter>();
        options.SchemaFilter<EnumNamesSchemaFilter>();
        options.SchemaFilter<RequiredNotNullableSchemaFilter>();
        options.OperationFilter<DefaultErrorResponsesFilter>();
        options.OperationFilter<HeaderParametersFilter>();
        options.DocumentFilter<UniqueOperationIdsFilter>();

        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new OpenApiInfo
            {
                Title = "Online Store API",
                Version = description.ApiVersion.ToString()
            });
        }

        var xmlFile = Path.Combine(AppContext.BaseDirectory, "Online Store Application(API).xml");
        if (File.Exists(xmlFile))
        {
            options.IncludeXmlComments(xmlFile);
        }

        var blXml = Path.Combine(AppContext.BaseDirectory, "BusinessLogic.xml");
        if (File.Exists(blXml))
        {
            options.IncludeXmlComments(blXml);
        }

        var appXml = Path.Combine(AppContext.BaseDirectory, "Application.xml");
        if (File.Exists(appXml))
        {
            options.IncludeXmlComments(appXml);
        }

        if (!options.SwaggerGeneratorOptions.SecuritySchemes.ContainsKey("Bearer"))
        {
            var securityScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter JWT Bearer token"
            };
            options.AddSecurityDefinition("Bearer", securityScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        }
    }
}
