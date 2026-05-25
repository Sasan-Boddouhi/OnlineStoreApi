// OnlineStore.Tests.Integration/Fixtures/DatabaseFixture.cs
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Entities;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Fixtures;

[CollectionDefinition("DatabaseCollection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // این کلاس فقط برای تعریف مجموعه است و کدی ندارد.
}

public class DatabaseFixture : IAsyncLifetime
{
    public IntegrationTestFactory Factory { get; }
    public int AdminEmployeeTypeId { get; private set; }
    public int TestCategoryId { get; private set; }
    public int TestSubcategoryId { get; private set; }

    public DatabaseFixture()
    {
        Factory = new IntegrationTestFactory();
    }

    public async Task InitializeAsync()
    {
        await Factory.InitializeAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. EmployeeType
        var adminType = await db.EmployeeType.FirstOrDefaultAsync(et => et.TypeName == "Admin");
        if (adminType == null)
        {
            adminType = new EmployeeType { TypeName = "Admin" };
            db.EmployeeType.Add(adminType);
            await db.SaveChangesAsync();
        }
        AdminEmployeeTypeId = adminType.EmployeeTypeId;

        // 2. ProductCategory
        var category = await db.ProductCategory.FirstOrDefaultAsync(c => c.CategoryName == "Electronics");
        if (category == null)
        {
            category = new ProductCategory { CategoryName = "Electronics", IsActive = true };
            db.ProductCategory.Add(category);
            await db.SaveChangesAsync();
        }
        TestCategoryId = category.CategoryId;

        // 3. ProductSubcategory
        var subcategory = await db.ProductSubcategory.FirstOrDefaultAsync(s => s.SubcategoryName == "Laptops" && s.CategoryId == TestCategoryId);
        if (subcategory == null)
        {
            subcategory = new ProductSubcategory
            {
                SubcategoryName = "Laptops",
                CategoryId = TestCategoryId,
                IsActive = true
            };
            db.ProductSubcategory.Add(subcategory);
            await db.SaveChangesAsync();
        }
        TestSubcategoryId = subcategory.SubcategoryId;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}