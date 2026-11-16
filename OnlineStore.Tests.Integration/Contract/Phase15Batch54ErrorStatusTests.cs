using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Contract;

/// <summary>
/// Batch 54 (API-06، BE-01) — وضعیت HTTP خطاهای سرویس‌ها با نوع خطا هم‌خوان است:
/// تکراری و «در حال استفاده» 409، نبودِ مرجع 404، بدون ورود 401؛ و کد خطا ثابت می‌ماند.
/// پیش از این، همهٔ این موارد 400 بودند.
/// </summary>
public class Phase15Batch54ErrorStatusTests : ControllerIntegrationTestBase
{
    public Phase15Batch54ErrorStatusTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task AuthAsAdminAsync() =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, raw);
        JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString().Should().Be(code, raw);
    }

    [Fact]
    public async Task DuplicateProvince_Returns409()
    {
        await AuthAsAdminAsync();
        var name = $"P54-{Guid.NewGuid():N}";
        (await Client.PostAsJsonAsync("/api/v1/provinces", new { ProvinceName = name })).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.PostAsJsonAsync("/api/v1/provinces", new { ProvinceName = name });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "PROVINCE_NAME_DUPLICATE");
    }

    [Fact]
    public async Task CityWithUnknownProvince_Returns404()
    {
        await AuthAsAdminAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/cities",
            new { CityName = $"C54-{Guid.NewGuid():N}", ProvinceId = 999999 });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "PROVINCE_NOT_FOUND");
    }

    [Fact]
    public async Task DuplicateCity_Returns409()
    {
        await AuthAsAdminAsync();
        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"P54-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();
        var name = $"C54-{Guid.NewGuid():N}";
        (await Client.PostAsJsonAsync("/api/v1/cities", new { CityName = name, province.ProvinceId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.PostAsJsonAsync("/api/v1/cities", new { CityName = name, province.ProvinceId });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "CITY_NAME_DUPLICATE");
    }

    [Fact]
    public async Task DeleteProvinceWithCities_Returns409()
    {
        await AuthAsAdminAsync();
        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"P54-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();
        db.City.Add(new City { CityName = $"C54-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId });
        await db.SaveChangesAsync();

        var response = await Client.DeleteAsync($"/api/v1/provinces/{province.ProvinceId}");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "PROVINCE_HAS_CITIES");
    }

    [Fact]
    public async Task DuplicateBrandSlug_Returns409()
    {
        await AuthAsAdminAsync();
        var slug = $"b54-{Guid.NewGuid():N}";
        (await Client.PostAsJsonAsync("/api/v1/brands", new { Name = $"B-{slug}", Slug = slug, IsActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.PostAsJsonAsync("/api/v1/brands", new { Name = $"B2-{slug}", Slug = slug, IsActive = true });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "BRAND_SLUG_DUPLICATE");
    }

    [Fact]
    public async Task DuplicateCouponCode_Returns409()
    {
        await AuthAsAdminAsync();
        var code = $"C54{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var body = new { Code = code, DiscountPercentage = 10, IsActive = true };
        (await Client.PostAsJsonAsync("/api/v1/coupons", body)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.PostAsJsonAsync("/api/v1/coupons", body);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "COUPON_CODE_DUPLICATE");
    }

    [Fact]
    public async Task DuplicateRoleCode_Returns409()
    {
        await AuthAsAdminAsync();
        var code = $"R54_{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var body = new { Code = code, DisplayName = "Role 54", IsActive = true, PermissionIds = new int[0] };
        (await Client.PostAsJsonAsync("/api/v1/roles", body)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Client.PostAsJsonAsync("/api/v1/roles", body);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "ROLE_CODE_DUPLICATE");
    }

    [Fact]
    public async Task PaymentInitiate_GatewayDown_Returns503WithRetryAfter()
    {
        await AuthAsAdminAsync();
        var db = GetService<AppDbContext>();
        var user = new User
        {
            FirstName = "B54", LastName = "Buyer", PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash", UserType = UserType.Customer, SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();
        var customer = new Customer { UserId = user.UserId };
        db.Customer.Add(customer);
        await db.SaveChangesAsync();
        var order = new Order { CustomerId = customer.CustomerId, ShippingFullName = "B54", ShippingAddress = "Tehran", ShippingPhoneNumber = "09120000000" };
        order.ApplyDiscountsAndShipping(0m, 100_000m, null, null);
        db.Order.Add(order);
        await db.SaveChangesAsync();
        Factory.PaymentGateway.NextInitiateFailure = PaymentInitiationResult.Fail("GATEWAY_ERROR", "ارتباط با درگاه برقرار نشد.");

        var response = await Client.PostAsJsonAsync("/api/v1/payments/initiate", new { OrderId = order.OrderId });

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "GATEWAY_ERROR");
        response.Headers.RetryAfter.Should().NotBeNull();
    }
}
