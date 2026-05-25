using System.Net;
using System.Net.Http.Json;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Builders;
using OnlineStore.Tests.Integration.Fixtures;
using Xunit;

namespace OnlineStore.Tests.Integration.Query;

[Collection("DatabaseCollection")]
public class DynamicQueryTests : IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly List<int> _createdProductIds = new();
    private int _testSubcategoryId;

    public DynamicQueryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        _testSubcategoryId = _fixture.TestSubcategoryId;

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // حذف داده‌های قبلی
        var existing = await db.Product.Where(p => p.Name.StartsWith("QueryTest_")).ToListAsync();
        db.Product.RemoveRange(existing);
        await db.SaveChangesAsync();

        // داده‌های تست
        var products = new[]
        {
            new ProductBuilder().WithName("QueryTest_Laptop_Dell").WithPrice(1200).WithSubcategoryId(_testSubcategoryId).Build(),
            new ProductBuilder().WithName("QueryTest_Phone_Xiaomi").WithPrice(300).WithSubcategoryId(_testSubcategoryId).Build(),
            new ProductBuilder().WithName("QueryTest_Tablet_Samsung").WithPrice(500).WithSubcategoryId(_testSubcategoryId).Build(),
            new ProductBuilder().WithName("QueryTest_Laptop_Apple").WithPrice(2500).WithSubcategoryId(_testSubcategoryId).Build(),
            new ProductBuilder().WithName("QueryTest_Phone_Apple").WithPrice(800).WithSubcategoryId(_testSubcategoryId).Build(),
            new ProductBuilder().WithName("QueryTest_Accessories").WithPrice(50).WithSubcategoryId(_testSubcategoryId).Build(),
        };
        await db.Product.AddRangeAsync(products);
        await db.SaveChangesAsync();

        _createdProductIds.AddRange(products.Select(p => p.ProductId));
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var productsToDelete = await db.Product
            .Where(p => p.Name.StartsWith("QueryTest_"))
            .ToListAsync();
        db.Product.RemoveRange(productsToDelete);
        await db.SaveChangesAsync();
    }

    #region Precedence & Parentheses

    [Fact]
    public async Task Filter_AndBeforeOr_RespectsPrecedence()
    {
        var response = await _client.GetAsync("/api/products?filter=price gt 1000 and name contains 'Laptop' or price lt 100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().Contain(p => p.Name.Contains("Dell") || p.Name.Contains("Apple") || p.Name == "QueryTest_Accessories");
    }

    [Fact]
    public async Task Filter_WithParentheses_ChangesLogic()
    {
        var response = await _client.GetAsync("/api/products?filter=price gt 1000 and (name contains 'Laptop' or name contains 'Phone')");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().OnlyContain(p => p.Price > 1000 && p.Name.Contains("Laptop"));
    }

    #endregion

    #region Invalid Tokens

    [Theory]
    [InlineData("price >>> 100")]
    [InlineData("name <<< 'test'")]
    [InlineData("price = = 100")]
    [InlineData("name eqeq 'x'")]
    [InlineData("price > 100")]
    public async Task Filter_InvalidTokens_ReturnsBadRequest(string filter)
    {
        var response = await _client.GetAsync($"/api/products?filter={Uri.EscapeDataString(filter)}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Nested Property Access

    [Fact]
    public async Task Filter_NestedProperty_SubcategoryName_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/products?filter=subcategory.subcategoryName eq 'Laptops'");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(p => p.SubcategoryId.Should().Be(_testSubcategoryId));
    }

    #endregion

    #region Null Handling (فعلاً غیرفعال - در صورت نیاز فعال کنید)

    [Fact]
    public async Task Filter_NullCheck_ExpirationDateEqualsNull_ReturnsProductsWithoutExpiration()
    {
        var response = await _client.GetAsync("/api/products?filter=ExpirationDate eq null");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
    }

    #endregion

    #region String Methods

    [Fact]
    public async Task Filter_StringContains_ReturnsMatching()
    {
        var response = await _client.GetAsync("/api/products?filter=name contains 'Phone'");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(p => p.Name.Should().Contain("Phone"));
    }

    [Fact]
    public async Task Filter_StringStartsWith_ReturnsMatching()
    {
        var response = await _client.GetAsync("/api/products?filter=name startsWith 'QueryTest_Laptop'");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(p => p.Name.Should().StartWith("QueryTest_Laptop"));
    }

    [Fact]
    public async Task Filter_StringEndsWith_ReturnsMatching()
    {
        var response = await _client.GetAsync("/api/products?filter=name endsWith 'Apple'");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(p => p.Name.Should().EndWith("Apple"));
    }

    #endregion

    #region Type Conversion Mismatch

    [Fact]
    public async Task Filter_StringComparedToNumber_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/products?filter=name eq 123");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Filter_NumberComparedToNonNumericString_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/products?filter=price eq 'abc'");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Injection-like Payloads

    [Theory]
    [InlineData("name eq 'x' or 1=1")]
    [InlineData("name eq 'x'; DROP TABLE Products; --")]
    [InlineData("price gt 0 UNION SELECT * FROM Users")]
    [InlineData("name eq 'x' AND 1=(SELECT COUNT(*) FROM Users)")]
    public async Task Filter_InjectionPayloads_ShouldNotThrowServerError(string filter)
    {
        var response = await _client.GetAsync($"/api/products?filter={Uri.EscapeDataString(filter)}");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Combined Query (Filter + Sort + Paging)

    [Fact]
    public async Task CombinedQuery_FilterSortPage_ReturnsCorrect()
    {
        var response = await _client.GetAsync("/api/products?filter=price gt 100&sort=-price&pageNumber=1&pageSize=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCountLessThanOrEqualTo(2);
        result.Items.Select(p => p.Price).Should().BeInDescendingOrder();
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    #endregion
}