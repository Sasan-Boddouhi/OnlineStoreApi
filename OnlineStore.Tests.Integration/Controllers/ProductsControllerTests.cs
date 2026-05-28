using System.Net;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using DataLayer.Context;
using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Controllers;

[Collection("DatabaseCollection")]
public class ProductsControllerIntegrationTests : BaseIntegrationTest
{
    private string _adminToken = string.Empty;

    public ProductsControllerIntegrationTests(
        IntegrationTestFactory<Program> factory)
        : base(factory)
    {
    }

    private async Task<string> GetAdminTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_adminToken))
            return _adminToken;

        var phone = $"0912{Random.Shared.Next(1000000, 9999999)}";

        // ================= REGISTER =================
        var registerDto = new RegisterDto
        {
            FirstName = "System",
            LastName = "Admin",
            PhoneNumber = phone,
            Password = "Password123!",
            DateOfBirth = "1370/01/01",
            DeviceId = Guid.NewGuid().ToString(),
            DeviceName = "IntegrationTest"
        };

        var registerResponse =
            await Client.PostAsJsonAsync("/api/auth/register", registerDto);

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // ================= PROMOTE TO ADMIN =================
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = db.User.First(x => x.PhoneNumber == phone);
            user.UserType = UserType.Employee;

            var adminType = db.EmployeeType
                .FirstOrDefault(x => x.TypeName == "Admin");

            if (adminType is null)
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
                UserId = user.UserId,
                EmployeeTypeId = adminType.EmployeeTypeId,
                EmployeeNumber = $"EMP{Random.Shared.Next(1000, 9999)}",
                HireDate = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // ================= LOGIN =================
        var loginResponse =
            await Client.PostAsJsonAsync("/api/auth/login", new LoginDto
            {
                PhoneNumber = phone,
                Password = "Password123!",
                DeviceId = Guid.NewGuid().ToString(),
                DeviceName = "IntegrationTest"
            });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result =
            await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();

        result.Should().NotBeNull();

        _adminToken = result!.AccessToken;
        return _adminToken;
    }

    [Fact]
    public async Task CreateProduct_Valid_ReturnsCreated()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        // دریافت SubcategoryId واقعی
        int subcategoryId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subcategory = db.ProductSubcategory.FirstOrDefault();
            if (subcategory == null)
                throw new InvalidOperationException("No subcategory seeded.");
            subcategoryId = subcategory.SubcategoryId;
        }

        // تغییر این خط: استفاده از ۸ کاراکتر به جای ۳ کاراکتر برای تضمین یکتا بودن نام
        var uniqueName = "TestProduct_" + Guid.NewGuid().ToString("N").Substring(0, 8); // نمونه خروجی: TestProduct_a1b2c3d4

        var dto = new CreateProductDto
        {
            Name = uniqueName,
            Price = 100,
            SubcategoryId = subcategoryId
        };

        var response = await Client.PostAsJsonAsync("/api/products", dto);

        // اگر باز هم خطا گرفت، لاگ را بررسی کنید
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Error: {response.StatusCode}, Details: {error}");
        }

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product.Should().NotBeNull();
        product!.Name.Should().Be(dto.Name);
    }

}