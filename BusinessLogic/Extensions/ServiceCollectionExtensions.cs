using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBusinessLogicServices(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();

            services.AddAutoMapper(typeof(ServiceCollectionExtensions).Assembly);

            // ثبت تمام سرویس‌ها به صورت داینامیک
            var serviceTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Service") && t.Name != "ServiceCollectionExtensions");

            foreach (var serviceType in serviceTypes)
            {
                var interfaceType = serviceType.GetInterfaces().FirstOrDefault(i => i.Name == $"I{serviceType.Name}");
                if (interfaceType != null)
                {
                    services.AddScoped(interfaceType, serviceType);
                }
            }

            return services;
        }
    }
}
