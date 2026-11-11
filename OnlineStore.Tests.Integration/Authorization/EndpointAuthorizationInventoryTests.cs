using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Asp.Versioning;
using BusinessLogic.DTOs.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Authorization;

/// <summary>
/// Batch 40 (SEC-01): فهرست کامل endpointها و مجوز مورد انتظار هرکدام.
/// هر endpoint تازه باید آگاهانه در این جدول ثبت شود؛ endpoint بدون هیچ ویژگی مجوز
/// با سیاست پیش‌فرض (fallback) فقط برای کاربر احراز هویت‌شده باز است.
/// </summary>
public sealed class EndpointAuthorizationInventoryTests : IClassFixture<IntegrationTestFactory<Program>>
{
    private const string Anonymous = "Anonymous";
    private const string Authenticated = "Authenticated";
    private const string AdminOnly = "Policy:AdminOnly";
    private const string CanManageCatalog = "Policy:CanManageCatalog";
    private const string AuthenticatedAdminOnly = "Authenticated + Policy:AdminOnly";

    /// <summary>کلید: «METHOD route»؛ مقدار: وضعیت مجوز مؤثر endpoint.</summary>
    private static readonly IReadOnlyDictionary<string, string> Expected = new Dictionary<string, string>
    {
        ["GET /health"] = Anonymous,
        ["GET /health/live"] = Anonymous,
        ["GET /health/ready"] = Anonymous,
        ["DELETE api/v{version:apiVersion}/Categories/{id:int}"] = CanManageCatalog,
        ["DELETE api/v{version:apiVersion}/Cities/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/EmployeeTypes/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/Employees/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/Listings/{id:int}"] = Authenticated,
        ["DELETE api/v{version:apiVersion}/Products/{id:int}"] = CanManageCatalog,
        ["DELETE api/v{version:apiVersion}/Products/{id:int}/variants/{variantId:int}"] = CanManageCatalog,
        ["DELETE api/v{version:apiVersion}/Provinces/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/Roles/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/Sellers/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/Subcategories/{id:int}"] = CanManageCatalog,
        ["DELETE api/v{version:apiVersion}/Users/me/addresses/{addressId:int}"] = Authenticated,
        ["DELETE api/v{version:apiVersion}/Users/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/Warehouses/{id:int}"] = Authenticated,
        ["DELETE api/v{version:apiVersion}/brands/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/carts/current"] = Anonymous,
        ["DELETE api/v{version:apiVersion}/carts/items/{listingId:int}"] = Anonymous,
        ["DELETE api/v{version:apiVersion}/coupons/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/products/{productId:int}/images/{imageId:int}"] = CanManageCatalog,
        ["DELETE api/v{version:apiVersion}/reviews/product/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/reviews/seller/{id:int}"] = AdminOnly,
        ["DELETE api/v{version:apiVersion}/shipping-methods/{id:int}"] = AdminOnly,
        ["GET /version"] = Anonymous,
        ["GET api/v{version:apiVersion}/Categories"] = Anonymous,
        ["GET api/v{version:apiVersion}/Categories/tree"] = Anonymous,
        ["GET api/v{version:apiVersion}/Categories/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Cities"] = Anonymous,
        ["GET api/v{version:apiVersion}/Cities/by-province/{provinceId:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Cities/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/EmployeeTypes"] = AdminOnly,
        ["GET api/v{version:apiVersion}/EmployeeTypes/{id:int}"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Employees"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Employees/me"] = Authenticated,
        ["GET api/v{version:apiVersion}/Employees/{id:int}"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Inventory/available/{listingId:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Invoices/by-order/{orderId:int}"] = Authenticated,
        ["GET api/v{version:apiVersion}/Invoices/my"] = Authenticated,
        ["GET api/v{version:apiVersion}/Invoices/{id:int}"] = Authenticated,
        ["GET api/v{version:apiVersion}/Listings"] = Anonymous,
        ["GET api/v{version:apiVersion}/Listings/variant/{variantId:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Listings/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Payments/order/{orderId:int}"] = Authenticated,
        ["GET api/v{version:apiVersion}/Products"] = Anonymous,
        ["GET api/v{version:apiVersion}/Products/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Products/{id:int}/attributes"] = Anonymous,
        ["GET api/v{version:apiVersion}/Products/{id:int}/variants"] = Anonymous,
        ["GET api/v{version:apiVersion}/Provinces"] = Anonymous,
        ["GET api/v{version:apiVersion}/Provinces/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Roles"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Roles/code/{code}"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Roles/permissions"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Roles/permissions/{id:int}"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Roles/{id:int}"] = AdminOnly,
        ["GET api/v{version:apiVersion}/SellerPayouts"] = AuthenticatedAdminOnly,
        ["GET api/v{version:apiVersion}/SellerPayouts/my"] = Authenticated,
        ["GET api/v{version:apiVersion}/Sellers"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Sellers/me"] = Authenticated,
        ["GET api/v{version:apiVersion}/Sellers/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Subcategories"] = Anonymous,
        ["GET api/v{version:apiVersion}/Subcategories/by-category/{categoryId:int}/count"] = Anonymous,
        ["GET api/v{version:apiVersion}/Subcategories/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/Users"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Users/me"] = Authenticated,
        ["GET api/v{version:apiVersion}/Users/me/addresses"] = Authenticated,
        ["GET api/v{version:apiVersion}/Users/roles"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Users/{id:int}"] = AdminOnly,
        ["GET api/v{version:apiVersion}/Warehouses"] = Authenticated,
        ["GET api/v{version:apiVersion}/Warehouses/{id:int}"] = Authenticated,
        ["GET api/v{version:apiVersion}/auth/me"] = Authenticated,
        ["GET api/v{version:apiVersion}/brands"] = Anonymous,
        ["GET api/v{version:apiVersion}/brands/{id:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/carts/current"] = Anonymous,
        ["GET api/v{version:apiVersion}/coupons"] = AdminOnly,
        ["GET api/v{version:apiVersion}/coupons/{id:int}"] = AdminOnly,
        ["GET api/v{version:apiVersion}/orders"] = Authenticated,
        ["GET api/v{version:apiVersion}/orders/admin"] = AuthenticatedAdminOnly,
        ["GET api/v{version:apiVersion}/orders/{id:int}"] = Authenticated,
        ["GET api/v{version:apiVersion}/products/{productId:int}/images"] = Anonymous,
        ["GET api/v{version:apiVersion}/reviews/product/pending"] = AdminOnly,
        ["GET api/v{version:apiVersion}/reviews/product/{productId:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/reviews/seller/pending"] = AdminOnly,
        ["GET api/v{version:apiVersion}/reviews/seller/{sellerId:int}"] = Anonymous,
        ["GET api/v{version:apiVersion}/seller-payouts"] = AuthenticatedAdminOnly,
        ["GET api/v{version:apiVersion}/seller-payouts/my"] = Authenticated,
        ["GET api/v{version:apiVersion}/sellers/orders/{orderId:int}"] = Authenticated,
        ["GET api/v{version:apiVersion}/sellers/{sellerId:int}/dashboard/recent-orders"] = Authenticated,
        ["GET api/v{version:apiVersion}/sellers/{sellerId:int}/dashboard/summary"] = Authenticated,
        ["GET api/v{version:apiVersion}/sellers/{sellerId:int}/dashboard/top-products"] = Authenticated,
        ["GET api/v{version:apiVersion}/shipping-methods"] = Anonymous,
        ["GET api/v{version:apiVersion}/shipping-methods/{id:int}"] = Anonymous,
        ["PATCH api/v{version:apiVersion}/Listings/{id:int}/price"] = Authenticated,
        ["PATCH api/v{version:apiVersion}/Users/{id:int}"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Categories"] = CanManageCatalog,
        ["POST api/v{version:apiVersion}/Cities"] = AdminOnly,
        ["POST api/v{version:apiVersion}/EmployeeTypes"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Employees"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Employees/{id:int}/terminate"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Inventory"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Inventory/add-stock/{listingId:int}"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Invoices"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/Invoices/{id:int}/pay"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/Listings"] = Authenticated,
        ["POST api/v{version:apiVersion}/Listings/{id:int}/inventory"] = Authenticated,
        ["POST api/v{version:apiVersion}/Payments/initiate"] = Authenticated,
        ["POST api/v{version:apiVersion}/Payments/verify"] = Authenticated,
        ["POST api/v{version:apiVersion}/Payments/webhook"] = Anonymous,
        ["POST api/v{version:apiVersion}/Payments/{paymentId:int}/refund"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Products"] = CanManageCatalog,
        ["POST api/v{version:apiVersion}/Products/{id:int}/attributes"] = CanManageCatalog,
        ["POST api/v{version:apiVersion}/Products/{id:int}/variants"] = CanManageCatalog,
        ["POST api/v{version:apiVersion}/Provinces"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Roles"] = AdminOnly,
        ["POST api/v{version:apiVersion}/SellerPayouts/{id:int}/cancel"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/SellerPayouts/{id:int}/pay"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/Sellers"] = Authenticated,
        ["POST api/v{version:apiVersion}/Subcategories"] = CanManageCatalog,
        ["POST api/v{version:apiVersion}/Users"] = AdminOnly,
        ["POST api/v{version:apiVersion}/Users/me/addresses"] = Authenticated,
        ["POST api/v{version:apiVersion}/Warehouses"] = Authenticated,
        ["POST api/v{version:apiVersion}/auth/login"] = Anonymous,
        ["POST api/v{version:apiVersion}/auth/logout"] = Authenticated,
        ["POST api/v{version:apiVersion}/auth/refresh"] = Anonymous,
        ["POST api/v{version:apiVersion}/auth/register"] = Anonymous,
        ["POST api/v{version:apiVersion}/brands"] = AdminOnly,
        ["POST api/v{version:apiVersion}/carts/checkout"] = Authenticated,
        ["POST api/v{version:apiVersion}/carts/items"] = Anonymous,
        ["POST api/v{version:apiVersion}/carts/merge"] = Authenticated,
        ["POST api/v{version:apiVersion}/coupons"] = AdminOnly,
        ["POST api/v{version:apiVersion}/orders/{id:int}/cancel"] = Authenticated,
        ["POST api/v{version:apiVersion}/orders/{id:int}/deliver"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/orders/{id:int}/return"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/products/{productId:int}/images"] = CanManageCatalog,
        ["POST api/v{version:apiVersion}/products/{productId:int}/images/url"] = CanManageCatalog,
        ["POST api/v{version:apiVersion}/reviews/product/{id:int}/reject"] = AdminOnly,
        ["POST api/v{version:apiVersion}/reviews/product/{productId:int}"] = Authenticated,
        ["POST api/v{version:apiVersion}/reviews/product/{reviewId:int}/approve"] = AdminOnly,
        ["POST api/v{version:apiVersion}/reviews/seller/{id:int}/reject"] = AdminOnly,
        ["POST api/v{version:apiVersion}/reviews/seller/{reviewId:int}/approve"] = AdminOnly,
        ["POST api/v{version:apiVersion}/reviews/seller/{sellerId:int}"] = Authenticated,
        ["POST api/v{version:apiVersion}/seller-payouts/{id:int}/cancel"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/seller-payouts/{id:int}/pay"] = AuthenticatedAdminOnly,
        ["POST api/v{version:apiVersion}/sellers/orders/{orderId:int}/groups/{groupId:int}/deliver"] = Authenticated,
        ["POST api/v{version:apiVersion}/sellers/orders/{orderId:int}/groups/{groupId:int}/ship"] = Authenticated,
        ["POST api/v{version:apiVersion}/shipping-methods"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Categories/{id:int}"] = CanManageCatalog,
        ["PUT api/v{version:apiVersion}/Cities/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/EmployeeTypes/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Employees/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Listings/{id:int}"] = Authenticated,
        ["PUT api/v{version:apiVersion}/Products/{id:int}"] = CanManageCatalog,
        ["PUT api/v{version:apiVersion}/Products/{id:int}/variants/{variantId:int}"] = CanManageCatalog,
        ["PUT api/v{version:apiVersion}/Provinces/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Roles/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Sellers/{id:int}"] = Authenticated,
        ["PUT api/v{version:apiVersion}/Sellers/{id:int}/admin"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Subcategories/{id:int}"] = CanManageCatalog,
        ["PUT api/v{version:apiVersion}/Users/me"] = Authenticated,
        ["PUT api/v{version:apiVersion}/Users/me/addresses/{addressId:int}"] = Authenticated,
        ["PUT api/v{version:apiVersion}/Users/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Users/{id:int}/roles"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/Warehouses/{id:int}"] = Authenticated,
        ["PUT api/v{version:apiVersion}/brands/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/carts/items/{listingId:int}"] = Anonymous,
        ["PUT api/v{version:apiVersion}/coupons/{id:int}"] = AdminOnly,
        ["PUT api/v{version:apiVersion}/shipping-methods/{id:int}"] = AdminOnly,
    };

    private readonly IntegrationTestFactory<Program> _factory;

    public EndpointAuthorizationInventoryTests(IntegrationTestFactory<Program> factory) => _factory = factory;

    [Fact]
    public void EveryEndpoint_HasTheExpectedAuthorization()
    {
        using var client = _factory.CreateClient();

        var actual = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .ToDictionary(Describe, Classify);

        var unexpected = actual.Keys.Except(Expected.Keys).Select(k => $"{k} => {actual[k]} (در جدول نیست)");
        var missing = Expected.Keys.Except(actual.Keys).Select(k => $"{k} (endpoint وجود ندارد)");
        var mismatched = actual.Keys.Intersect(Expected.Keys)
            .Where(k => actual[k] != Expected[k])
            .Select(k => $"{k}: انتظار {Expected[k]}، واقعی {actual[k]}");

        unexpected.Concat(missing).Concat(mismatched).Should().BeEmpty();
    }

    [Fact]
    public async Task EndpointWithoutAuthorizationMetadata_RequiresAnAuthenticatedUser()
    {
        await _factory.InitializeDatabaseAsync();
        using var factory = WithProbeController();
        using var client = factory.CreateClient();

        var anonymous = await client.GetAsync(FallbackProbeController.Route);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { PhoneNumber = "09123456789", Password = "Test@123", DeviceId = "fallback-probe" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<AuthResultDto>())!.AccessToken;

        using var request = new HttpRequestMessage(HttpMethod.Get, FallbackProbeController.Route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var authenticated = await client.SendAsync(request);
        authenticated.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/version")]
    public async Task HealthAndVersionEndpoints_StayAnonymous(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnknownRoute_StillReturnsNotFound_ForAnonymousCaller()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/no-such-endpoint");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithProbeController() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(FallbackProbeController).Assembly)));

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
        var route = endpoint.RoutePattern.RawText ?? string.Empty;
        return $"{string.Join(",", methods)} {route}";
    }

    private static string Classify(Endpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
            return Anonymous;

        var tokens = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(d => !string.IsNullOrEmpty(d.Policy) ? $"Policy:{d.Policy}"
                : !string.IsNullOrEmpty(d.Roles) ? $"Roles:{d.Roles}"
                : Authenticated)
            .Concat(endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>().Select(_ => "InlinePolicy"))
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return tokens.Count == 0 ? "None (fallback)" : string.Join(" + ", tokens);
    }
}

/// <summary>
/// کنترلر آزمایشی بدون هیچ ویژگی مجوز؛ فقط در تست fallback به برنامه افزوده می‌شود.
/// </summary>
[ApiController]
[ApiVersionNeutral]
[Route(Route)]
public sealed class FallbackProbeController : ControllerBase
{
    public const string Route = "test/fallback-probe";

    [HttpGet]
    public IActionResult Get() => Ok(new { reached = true });
}
