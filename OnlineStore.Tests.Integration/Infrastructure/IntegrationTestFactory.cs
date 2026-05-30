using DataLayer.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class IntegrationTestFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private readonly DbConnection _connection = CreateInMemoryConnection();

    private static SqliteConnection CreateInMemoryConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddJsonFile("appsettings.Testing.json", optional: true);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "THIS_IS_A_VERY_SECRET_TEST_KEY_1234567890",
                ["Jwt:Issuer"] = "OnlineStoreApi",
                ["Jwt:Audience"] = "OnlineStoreClient",
                ["Jwt:ExpireMinutes"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);

            var appDbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(AppDbContext));
            if (appDbDescriptor != null) services.Remove(appDbDescriptor);

            // Interceptor
            services.AddScoped<ISaveChangesInterceptor, SqliteRowVersionFixInterceptor>();

            // ثبت دستی DbContextOptions و AppDbContext -> TestAppDbContext
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite(_connection);
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
            services.AddScoped(_ => optionsBuilder.Options);

            services.AddScoped<AppDbContext, TestAppDbContext>();
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        await TestDataSeed.SeedAsync(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Dispose();
    }
}