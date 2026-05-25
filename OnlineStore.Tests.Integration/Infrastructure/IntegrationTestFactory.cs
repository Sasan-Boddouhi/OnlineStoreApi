using Application.Entities;
using DataLayer.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private IConfiguration _configuration = null!;
    private string _connectionString = null!;

    /// <summary>
    /// رشته اتصال به دیتابیس تست (برای استفاده در Respawner و ...)
    /// </summary>
    public string GetConnectionString() => _connectionString;

    /// <summary>
    /// اجرای migration و آماده‌سازی دیتابیس قبل از اجرای تست‌ها
    /// </summary>
    public async Task InitializeAsync()
    {
        // بارگذاری تنظیمات از فایل appsettings.Test.json در پروژه تست
        var testProjectPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(testProjectPath!)
            .AddJsonFile("appsettings.Test.json", optional: false);
        _configuration = configBuilder.Build();
        _connectionString = _configuration.GetConnectionString("SQLServer")!;

        // اجرای migration روی دیتابیس تست
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        using var context = new AppDbContext(options, Enumerable.Empty<ISaveChangesInterceptor>());
        await context.Database.MigrateAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.EmployeeType.Any())
        {
            db.EmployeeType.Add(new EmployeeType { TypeName = "Admin" });
            db.EmployeeType.Add(new EmployeeType { TypeName = "Manager" });
            db.EmployeeType.Add(new EmployeeType { TypeName = "Employee" });
            await db.SaveChangesAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // بارگذاری فایل پیکربندی تست (برای JWT و ...)
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testProjectPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            config.SetBasePath(testProjectPath!);
            config.AddJsonFile("appsettings.Test.json", optional: false);
        });

        builder.ConfigureServices(services =>
        {
            // حذف DbContext اصلی (که در Program.cs ثبت شده)
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            // ثبت DbContext سفارشی با Interceptors خالی
            services.AddScoped<AppDbContext>(sp =>
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(_connectionString)
                    .Options;
                return new AppDbContext(options, Enumerable.Empty<ISaveChangesInterceptor>());
            });

            // در صورت نیاز، سایر سرویس‌های وابسته به DbContext را نیز می‌توان بازنویسی کرد
            // اما معمولاً همین کافی است
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // در صورت نیاز، دیتابیس را پاک کنید (اختیاری)
        // var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options;
        // using var context = new AppDbContext(options, Enumerable.Empty<ISaveChangesInterceptor>());
        // await context.Database.EnsureDeletedAsync();
        await Task.CompletedTask;
    }
}