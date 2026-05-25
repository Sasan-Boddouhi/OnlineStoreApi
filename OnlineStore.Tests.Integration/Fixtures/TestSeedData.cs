using Application.Entities;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace OnlineStore.Tests.Integration.Fixtures;

public static class TestSeedData
{
    public static async Task SeedAsync(AppDbContext dbContext)
    {
        // 1. EmployeeType
        if (!await dbContext.EmployeeType.AnyAsync())
        {
            dbContext.EmployeeType.AddRange(
                new EmployeeType { TypeName = "Admin" },
                new EmployeeType { TypeName = "Manager" },
                new EmployeeType { TypeName = "Employee" }
            );
            await dbContext.SaveChangesAsync();
        }

        // 2. ProductCategory و ProductSubcategory
        if (!await dbContext.ProductCategory.AnyAsync(c => c.CategoryName == "Electronics"))
        {
            var electronics = new ProductCategory { CategoryName = "Electronics", IsActive = true };
            dbContext.ProductCategory.Add(electronics);
            await dbContext.SaveChangesAsync();

            var laptops = new ProductSubcategory
            {
                SubcategoryName = "Laptops",
                CategoryId = electronics.CategoryId,
                IsActive = true
            };
            dbContext.ProductSubcategory.Add(laptops);
            await dbContext.SaveChangesAsync();
        }

        // 3. کاربر ادمین (User + Employee)
        if (!await dbContext.User.AnyAsync(u => u.PhoneNumber == "09120000000"))
        {
            var adminUser = new User
            {
                FirstName = "Admin",
                LastName = "Test",
                PhoneNumber = "09120000000",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                UserType = UserType.Employee,
                IsActive = true,
                Email = "admin@test.com",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            dbContext.User.Add(adminUser);
            await dbContext.SaveChangesAsync();

            var adminType = await dbContext.EmployeeType.FirstAsync(et => et.TypeName == "Admin");
            var employee = new Employee
            {
                UserId = adminUser.UserId,
                EmployeeTypeId = adminType.EmployeeTypeId,
                EmployeeNumber = "EMP_ADMIN_001",
                Salary = 10000,
                HireDate = DateTime.UtcNow
            };
            dbContext.Employee.Add(employee);
            await dbContext.SaveChangesAsync();
        }
    }
}