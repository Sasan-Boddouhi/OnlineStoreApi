using Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Online_Store_Application.Services;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// F-04: میزبان تست با کش خروجی روشن (store درون‌حافظه‌ای).
/// کارخانهٔ پیش‌فرض کش خروجی را خاموش می‌کند، و به همین دلیل نشت سبد مهمان هیچ‌وقت دیده نشد.
/// اینجا همچنین سرویس واقعی کاربر جاری استفاده می‌شود؛ نسخهٔ Fake درخواست بی‌توکن را کاربر ۱ فرض می‌کند
/// و در آن صورت همهٔ «مهمان‌ها» یک سبد مشترک می‌گرفتند، حتی بدون کش.
/// </summary>
public sealed class OutputCacheEnabledFactory : IntegrationTestFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("OutputCaching:Enabled", "true");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICurrentUserService>();
            services.AddScoped<ICurrentUserService, CurrentUserService>();
        });
    }
}
