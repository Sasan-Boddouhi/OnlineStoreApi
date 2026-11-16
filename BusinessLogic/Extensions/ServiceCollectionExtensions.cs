using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using Application.Interfaces;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BusinessLogic.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBusinessLogicServices(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();

            // Batch 40 (ARC-05): سقف عمق برای نگاشت‌های خودارجاع؛ روی پیکربندی سراسری و همهٔ Profileها اعمال می‌شود.
            services.AddAutoMapper(AutoMapperDepthGuard.Apply, typeof(ServiceCollectionExtensions).Assembly);

            var serviceTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsClass &&
                            !t.IsAbstract &&
                            t.Name.EndsWith("Service") &&
                            t.Name != nameof(ServiceCollectionExtensions));

            foreach (var serviceType in serviceTypes)
            {
                var interfaceType = serviceType
                    .GetInterfaces()
                    .FirstOrDefault(i => i.Name == $"I{serviceType.Name}");

                if (interfaceType != null)
                    services.AddScoped(interfaceType, serviceType);
            }

            // Phase 9 (R44): نام این کلاس قرارداد XService/IXService را برآورده نمی‌کند،
            // پس ثبت دستی لازم است. اگر ثبت نشود، job در زمان اجرا سقوط می‌کند
            // ولی تست‌های واحد آن را نمی‌گیرند — فقط تست یکپارچه.
            services.AddScoped<IAbandonedPaymentSweeper, AbandonedPaymentSweeper>();

            return services;
        }

        public static IServiceCollection AddFluentValidationServices(this IServiceCollection services)
        {
            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), ServiceLifetime.Scoped);

            services.AddFluentValidationAutoValidation();

            return services;
        }
    }
}