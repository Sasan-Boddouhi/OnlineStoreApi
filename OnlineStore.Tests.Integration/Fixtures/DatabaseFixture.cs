using Application.Entities;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Fixtures;

[CollectionDefinition("DatabaseCollection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
}

public class DatabaseFixture : IAsyncLifetime
{
    public IntegrationTestFactory Factory { get; }

    public int AdminEmployeeTypeId { get; private set; }

    public int TestCategoryId { get; private set; }

    public int TestSubcategoryId { get; private set; }

    private DatabaseRespawner _respawner = null!;

    public DatabaseFixture()
    {
        Factory = new IntegrationTestFactory();
    }

    public async Task InitializeAsync()
    {
        await Factory.InitializeAsync();

        _respawner = new DatabaseRespawner(Factory.GetConnectionString());
        await _respawner.InitializeAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await TestSeedData.SeedAsync(db); 

        // خواندن مقادیر مورد نیاز برای تست‌ها
        TestCategoryId = await db.ProductCategory
            .Where(c => c.CategoryName == "Electronics")
            .Select(c => c.CategoryId)
            .FirstAsync();
        TestSubcategoryId = await db.ProductSubcategory
            .Where(s => s.SubcategoryName == "Laptops")
            .Select(s => s.SubcategoryId)
            .FirstAsync();
        AdminEmployeeTypeId = await db.EmployeeType
            .Where(et => et.TypeName == "Admin")
            .Select(et => et.EmployeeTypeId)
            .FirstAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // فقط جداول اصلی را خالی کن (به جز MigrationHistory)
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Product");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Employee");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [User]");
        // ... بقیه جداول
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}