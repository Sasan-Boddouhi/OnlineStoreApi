using Application.Entities;
using DataLayer.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private IConfiguration _configuration = null!;
    private string _connectionString = null!;

    public string GetConnectionString()
    {
        return _connectionString;
    }

    public async Task InitializeAsync()
    {
        var testProjectPath = Directory.GetCurrentDirectory();

        _configuration = new ConfigurationBuilder()
            .SetBasePath(testProjectPath)
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        _connectionString = _configuration.GetConnectionString("SQLServer")!;

        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        if (!await db.EmployeeType.AnyAsync())
        {
            db.EmployeeType.Add(new EmployeeType
            {
                TypeName = "Admin"
            });

            db.EmployeeType.Add(new EmployeeType
            {
                TypeName = "Manager"
            });

            db.EmployeeType.Add(new EmployeeType
            {
                TypeName = "Employee"
            });

            await db.SaveChangesAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testProjectPath = Directory.GetCurrentDirectory();

            config.SetBasePath(testProjectPath);

            config.AddJsonFile("appsettings.Test.json", optional: false);
        });

        builder.ConfigureServices(services =>
        {
            var descriptors = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(AppDbContext))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(_connectionString);
            });
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.CompletedTask;
    }
}