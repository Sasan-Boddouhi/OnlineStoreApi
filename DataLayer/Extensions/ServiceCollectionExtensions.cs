using Application.Interfaces;
using Application.Options;
using DataLayer.Context;
using DataLayer.Repositories;
using DataLayer.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataLayer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataLayerServices(
        this IServiceCollection services,
        DatabaseOptions databaseOptions) 
    {
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(databaseOptions.DefaultConnection);
            var auditInterceptor = serviceProvider.GetRequiredService<AuditInterceptor>();
            options.AddInterceptors(auditInterceptor);
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        return services;
    }
}