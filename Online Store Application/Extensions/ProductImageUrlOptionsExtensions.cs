using Application.Options;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Extensions;

/// <summary>Batch 48c: عضو نامعتبر در ProductImages:AllowedExternalHosts در همهٔ محیط‌ها راه‌اندازی را متوقف می‌کند.</summary>
public sealed class ProductImageUrlOptionsValidator : IValidateOptions<ProductImageUrlOptions>
{
    public ValidateOptionsResult Validate(string? name, ProductImageUrlOptions options)
    {
        var errors = options.Validate();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public static class ProductImageUrlOptionsExtensions
{
    public static IServiceCollection AddProductImageUrlOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProductImageUrlOptions>()
            .Bind(configuration.GetSection(ProductImageUrlOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ProductImageUrlOptions>, ProductImageUrlOptionsValidator>();
        return services;
    }
}
