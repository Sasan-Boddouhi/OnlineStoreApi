using Application.Entities;
using Application.Helper;
using Application.Interfaces.Security;
using DataLayer.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class IntegrationTestFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private IConfiguration _configuration = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var projectDir = Directory.GetCurrentDirectory();

            config.SetBasePath(projectDir)
                  .AddJsonFile("appsettings.Test.json", optional: false, reloadOnChange: true);
        });

        builder.ConfigureServices(services =>
        {
            // ================= REMOVE REAL DB =================
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));

            if (descriptor != null)
                services.Remove(descriptor);

            // ================= ADD TEST DB =================
            var sp = services.BuildServiceProvider();
            _configuration = sp.GetRequiredService<IConfiguration>();

            var connectionString = _configuration.GetConnectionString("SQLServer");

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableSensitiveDataLogging();
                options.LogTo(Console.WriteLine, LogLevel.Information);
            });
        });
    }

    // ================= INIT DATABASE =================
    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await db.Database.MigrateAsync();
        await TestDataSeed.SeedAsync(db);

        // ================= ADMIN USER =================
        // استفاده از یک شماره موبایل واقعی 11 رقمی
        string adminPhone = "09123456789";
        if (!db.User.Any(u => u.PhoneNumber == adminPhone))
        {
            var admin = new User
            {
                PhoneNumber = adminPhone,
                PasswordHash = hasher.Hash("123456"),
                IsActive = true,
                UserType = UserType.Employee,
                SecurityStamp = Guid.NewGuid().ToString(),
                FirstName = "Admin",
                LastName = "System"
            };

            db.User.Add(admin);
            await db.SaveChangesAsync();

            var adminType = db.EmployeeType.FirstOrDefault(x => x.TypeName == "Admin");
            if (adminType == null)
            {
                adminType = new EmployeeType
                {
                    TypeName = "Admin",
                    DisplayName = "ادمین",
                    IsSystem = true,
                    IsActive = true
                };
                db.EmployeeType.Add(adminType);
                await db.SaveChangesAsync();
            }

            db.Employee.Add(new Employee
            {
                UserId = admin.UserId,
                EmployeeTypeId = adminType.EmployeeTypeId,
                EmployeeNumber = "ADMIN001",   // کوتاه‌تر
                HireDate = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
    }
}