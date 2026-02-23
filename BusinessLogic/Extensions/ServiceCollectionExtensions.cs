using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using System.Reflection;

namespace BusinessLogic.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBusinessLogicServices(this IServiceCollection services)
        {
            services.AddHttpContextAccessor();

            services.AddAutoMapper(Assembly.GetExecutingAssembly());

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

            return services;
        }

        public static IServiceCollection AddFluentValidationServices(this IServiceCollection services)
        {
            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

            services.AddFluentValidationAutoValidation();

            return services;
        }
    }
}