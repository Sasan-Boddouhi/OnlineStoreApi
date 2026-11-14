using Application.Options;
using Microsoft.Extensions.Options;
using Online_Store_Application.Services;

namespace Online_Store_Application.Extensions;

/// <summary>
/// Batch 49b: اگر بخش <c>PlatformWarehouse</c> (دست‌کم یکی از فیلدهای نشانی) تنظیم شده باشد، باید کامل و معتبر باشد؛
/// در هر محیطی راه‌اندازی را متوقف می‌کند تا اشتباه تایپی دیر کشف نشود. بخش تنظیم‌نشده اینجا معتبر است:
/// اینکه نبودنش در Production مجاز است یا نه به وجود انبار پلتفرم در پایگاه داده بستگی دارد
/// و <see cref="PlatformWarehouseBootstrapper"/> آن را می‌سنجد.
/// </summary>
public sealed class PlatformWarehouseOptionsValidator : IValidateOptions<PlatformWarehouseOptions>
{
    public ValidateOptionsResult Validate(string? name, PlatformWarehouseOptions options)
    {
        if (!options.IsSpecified)
            return ValidateOptionsResult.Success;

        var errors = options.Validate();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public static class PlatformWarehouseOptionsExtensions
{
    /// <summary>تنظیمات انبار پلتفرم (ValidateOnStart) و بوت‌استرپ آن هنگام راه‌اندازی.</summary>
    public static IServiceCollection AddPlatformWarehouseBootstrap(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PlatformWarehouseOptions>()
            .Bind(configuration.GetSection(PlatformWarehouseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PlatformWarehouseOptions>, PlatformWarehouseOptionsValidator>();
        services.AddHostedService<PlatformWarehouseBootstrapper>();
        return services;
    }
}
