using Application.Entities;
using Application.Interfaces.Security;
using DataLayer.Context;
using DataLayer.Security;
using Microsoft.EntityFrameworkCore;

namespace OnlineStore.Tests.Integration.Infrastructure;

public static class TestDataSeed
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher? passwordHasher = null)
    {
        passwordHasher ??= new BcryptPasswordHasher();

        // ۱. نوع کارمند Admin
        var adminType = await db.EmployeeType
            .FirstOrDefaultAsync(et => et.TypeName == "Admin");
        if (adminType == null)
        {
            adminType = new EmployeeType
            {
                TypeName = "Admin",
                DisplayName = "مدیر سیستم"
            };
            db.EmployeeType.Add(adminType);
            await db.SaveChangesAsync();
        }

        // ۲. کاربر ادمین
        if (!await db.User.AnyAsync(u => u.PhoneNumber == "09123456789"))
        {
            var user = new User
            {
                PhoneNumber = "09123456789",
                PasswordHash = passwordHasher.Hash("Test@123"),
                FirstName = "ادمین",
                LastName = "تست",
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                UserType = UserType.Employee
            };
            db.User.Add(user);
            await db.SaveChangesAsync();

            db.Employee.Add(new Employee
            {
                UserId = user.UserId,
                EmployeeTypeId = adminType.EmployeeTypeId,
                EmployeeNumber = "E-001",
                // در صورت نیاز Salary و HireDate را هم بدهید (اگر required هستند)
            });
            await db.SaveChangesAsync();
        }

        // ۳. دسته‌بندی‌ها
        if (!await db.ProductCategory.AnyAsync())
        {
            var cat = new ProductCategory
            {
                CategoryName = "Test Category"
            };
            db.ProductCategory.Add(cat);
            await db.SaveChangesAsync();
        }

        var category = await db.ProductCategory.FirstAsync();

        if (!await db.ProductSubcategory.AnyAsync())
        {
            db.ProductSubcategory.Add(new ProductSubcategory
            {
                SubcategoryName = "Test Subcategory",
                CategoryId = category.CategoryId
            });
            await db.SaveChangesAsync();
        }
    }
}