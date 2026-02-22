using Application.Interfaces;
using DataLayer.Context;
using DataLayer.Repositories;
using DataLayer.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDataLayerServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<AuditInterceptor>();
            services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            {
                options.UseSqlServer(configuration.GetConnectionString("SQLServer"));

                var auditInterceptor = serviceProvider.GetRequiredService<AuditInterceptor>();
                options.AddInterceptors(auditInterceptor);
            });

            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

            return services;
        }

    }
}
