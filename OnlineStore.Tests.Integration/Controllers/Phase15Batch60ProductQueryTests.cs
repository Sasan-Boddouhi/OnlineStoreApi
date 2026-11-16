using System.Net;
using System.Text.Json;
using Application.Entities;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 60 — درستی تجزیه‌گر فیلتر (F-24) و پارامترهای نوع‌دار فهرست محصولات (API-10).
///
///  • فیلتر با چند شرط کامل اعمال می‌شود؛ توکن اضافه یا ناقص ← 400 با کد INVALID_QUERY.
///  • <c>q</c>، <c>categoryId</c>، <c>subcategoryId</c>، <c>brandId</c>، <c>inStock</c> و
///    <c>sort=newest|rating|name</c> با هم و با <c>filter</c> به‌صورت AND ترکیب می‌شوند.
///  • مقدار نامعتبر پارامتر نوع‌دار (مرتب‌سازی ناشناخته، شناسهٔ منفی) ← 422 VALIDATION_ERROR.
/// </summary>
public class Phase15Batch60ProductQueryTests : ControllerIntegrationTestBase
{
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase15Batch60ProductQueryTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== F-24: فیلتر ====================

    [Fact]
    public async Task Filter_WithTwoConditions_AppliesBoth()
    {
        var tag = NewTag();
        var subcategoryId = await SeedSubcategoryAsync();
        var cheap = await SeedProductAsync($"{tag}-cheap", subcategoryId, price: 10m);
        var expensive = await SeedProductAsync($"{tag}-expensive", subcategoryId, price: 1000m);

        var ids = await GetIdsAsync($"filter={Q($"Name contains '{tag}' and Price gt 100")}");

        ids.Should().Equal(expensive);
        ids.Should().NotContain(cheap);
    }

    [Theory]
    [InlineData("Name eq 'x' garbage")]
    [InlineData("Name eq 'x' Price gt 1")]
    [InlineData("Name eq 'x' and")]
    [InlineData("Name eq")]
    public async Task Filter_WithTrailingOrMissingTokens_Returns400InvalidQuery(string filter)
    {
        var response = await Client.GetAsync($"/api/v1/products?filter={Q(filter)}");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "INVALID_QUERY");
    }

    // ==================== API-10: پارامترهای نوع‌دار ====================

    [Fact]
    public async Task Q_MatchesNameOrDescription()
    {
        var tag = NewTag();
        var subcategoryId = await SeedSubcategoryAsync();
        var byName = await SeedProductAsync($"{tag} phone", subcategoryId);
        var byDescription = await SeedProductAsync($"other-{Guid.NewGuid():N}", subcategoryId, description: $"a {tag} inside");
        await SeedProductAsync($"unrelated-{Guid.NewGuid():N}", subcategoryId);

        var ids = await GetIdsAsync($"q={Q(tag)}");

        ids.Should().BeEquivalentTo(new[] { byName, byDescription });
    }

    [Fact]
    public async Task CategoryId_ReturnsOnlyProductsOfThatCategory()
    {
        var tag = NewTag();
        var (categoryA, subA) = await SeedCategoryAsync();
        var (_, subB) = await SeedCategoryAsync();
        var inA = await SeedProductAsync($"{tag}-a", subA);
        await SeedProductAsync($"{tag}-b", subB);

        var ids = await GetIdsAsync($"categoryId={categoryA}");

        ids.Should().Equal(inA);
    }

    [Fact]
    public async Task SubcategoryId_ReturnsOnlyProductsOfThatSubcategory()
    {
        var tag = NewTag();
        var (categoryId, subA) = await SeedCategoryAsync();
        var subB = await SeedSubcategoryAsync(categoryId);
        var inA = await SeedProductAsync($"{tag}-a", subA);
        await SeedProductAsync($"{tag}-b", subB);

        var ids = await GetIdsAsync($"subcategoryId={subA}");

        ids.Should().Equal(inA);
    }

    [Fact]
    public async Task BrandId_ReturnsOnlyProductsOfThatBrand()
    {
        var tag = NewTag();
        var subcategoryId = await SeedSubcategoryAsync();
        var brandA = await SeedBrandAsync();
        var brandB = await SeedBrandAsync();
        var ofA = await SeedProductAsync($"{tag}-a", subcategoryId, brandId: brandA);
        await SeedProductAsync($"{tag}-b", subcategoryId, brandId: brandB);
        await SeedProductAsync($"{tag}-none", subcategoryId);

        var ids = await GetIdsAsync($"brandId={brandA}");

        ids.Should().Equal(ofA);
    }

    [Fact]
    public async Task InStock_FiltersBySellableStock()
    {
        var tag = NewTag();
        var subcategoryId = await SeedSubcategoryAsync();
        var stocked = await SeedProductAsync($"{tag}-stocked", subcategoryId);
        await SeedListingAsync(stocked, quantity: 5m);
        var soldOut = await SeedProductAsync($"{tag}-soldout", subcategoryId);
        await SeedListingAsync(soldOut, quantity: 0m);
        var noListing = await SeedProductAsync($"{tag}-nolisting", subcategoryId);

        (await GetIdsAsync($"q={Q(tag)}&inStock=true")).Should().Equal(stocked);
        (await GetIdsAsync($"q={Q(tag)}&inStock=false")).Should().BeEquivalentTo(new[] { soldOut, noListing });
    }

    [Fact]
    public async Task TypedParameters_AndFilter_AreCombinedWithAnd()
    {
        var tag = NewTag();
        var (categoryId, subcategoryId) = await SeedCategoryAsync();
        var otherSub = await SeedSubcategoryAsync();
        var match = await SeedProductAsync($"{tag}-match", subcategoryId, price: 500m);
        await SeedProductAsync($"{tag}-cheap", subcategoryId, price: 5m);
        await SeedProductAsync($"{tag}-othercat", otherSub, price: 500m);

        var ids = await GetIdsAsync($"q={Q(tag)}&categoryId={categoryId}&filter={Q("Price ge 100")}");

        ids.Should().Equal(match);
    }

    [Fact]
    public async Task SortNewest_OrdersByCreationTimeDescending()
    {
        var tag = NewTag();
        var subcategoryId = await SeedSubcategoryAsync();
        var oldest = await SeedProductAsync($"{tag}-1", subcategoryId);
        var middle = await SeedProductAsync($"{tag}-2", subcategoryId);
        var newest = await SeedProductAsync($"{tag}-3", subcategoryId);
        await SetCreatedOnAsync(oldest, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedOnAsync(middle, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await SetCreatedOnAsync(newest, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var ids = await GetIdsAsync($"q={Q(tag)}&sort=newest");

        ids.Should().Equal(newest, middle, oldest);
    }

    [Fact]
    public async Task SortRating_OrdersByAverageRatingDescending()
    {
        var tag = NewTag();
        var subcategoryId = await SeedSubcategoryAsync();
        var low = await SeedProductAsync($"{tag}-low", subcategoryId, rating: 2.0);
        var high = await SeedProductAsync($"{tag}-high", subcategoryId, rating: 4.5);
        var mid = await SeedProductAsync($"{tag}-mid", subcategoryId, rating: 3.0);

        var ids = await GetIdsAsync($"q={Q(tag)}&sort=rating");

        ids.Should().Equal(high, mid, low);
    }

    [Fact]
    public async Task SortName_OrdersByNameAscending_AndLegacyDescendingSortStillWorks()
    {
        var tag = NewTag();
        var subcategoryId = await SeedSubcategoryAsync();
        var c = await SeedProductAsync($"{tag}-c", subcategoryId);
        var a = await SeedProductAsync($"{tag}-a", subcategoryId);
        var b = await SeedProductAsync($"{tag}-b", subcategoryId);

        (await GetIdsAsync($"q={Q(tag)}&sort=name")).Should().Equal(a, b, c);
        (await GetIdsAsync($"q={Q(tag)}&sort=-name")).Should().Equal(c, b, a);
    }

    [Theory]
    [InlineData("sort=cheapest")]
    [InlineData("sort=newest,name")]
    [InlineData("sort=noSuchField")]
    public async Task UnknownSort_Returns422(string query)
    {
        var response = await Client.GetAsync($"/api/v1/products?{query}");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("categoryId=-1")]
    [InlineData("subcategoryId=0")]
    [InlineData("brandId=-5")]
    [InlineData("categoryId=abc")]
    [InlineData("inStock=maybe")]
    public async Task InvalidTypedParameter_Returns422(string query)
    {
        var response = await Client.GetAsync($"/api/v1/products?{query}");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task TooLongQ_Returns422()
    {
        var response = await Client.GetAsync($"/api/v1/products?q={new string('x', 101)}");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
    }

    // ==================== helpers ====================

    private static string NewTag() => $"b60{Guid.NewGuid():N}"[..20];

    private static string Q(string value) => Uri.EscapeDataString(value);

    private async Task<List<int>> GetIdsAsync(string query)
    {
        var response = await Client.GetAsync($"/api/v1/products?pageSize=100&{query}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("productId").GetInt32())
            .ToList();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be(code, body);
    }

    private async Task<(int CategoryId, int SubcategoryId)> SeedCategoryAsync()
    {
        var category = new ProductCategory { CategoryName = $"B60Cat-{Guid.NewGuid():N}" };
        DbContext.ProductCategory.Add(category);
        await DbContext.SaveChangesAsync();
        var subcategoryId = await SeedSubcategoryAsync(category.CategoryId);
        return (category.CategoryId, subcategoryId);
    }

    private async Task<int> SeedSubcategoryAsync(int? categoryId = null)
    {
        if (categoryId is null)
            return (await SeedCategoryAsync()).SubcategoryId;

        var subcategory = new ProductSubcategory { SubcategoryName = $"B60Sub-{Guid.NewGuid():N}", CategoryId = categoryId.Value };
        DbContext.ProductSubcategory.Add(subcategory);
        await DbContext.SaveChangesAsync();
        return subcategory.SubcategoryId;
    }

    private async Task<int> SeedBrandAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var brand = new Brand { Name = $"B60Brand-{suffix}", Slug = $"b60-{suffix}" };
        DbContext.Brand.Add(brand);
        await DbContext.SaveChangesAsync();
        return brand.BrandId;
    }

    private async Task<int> SeedProductAsync(
        string name,
        int subcategoryId,
        decimal price = 100m,
        string? description = null,
        int? brandId = null,
        double rating = 0)
    {
        var product = new Product
        {
            Name = name,
            Description = description,
            Price = price,
            SubcategoryId = subcategoryId,
            BrandId = brandId,
            Unit = UnitOfMeasurement.Piece,
            IsActive = true,
            AverageRating = rating,
            ReviewCount = rating > 0 ? 1 : 0
        };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return product.ProductId;
    }

    private async Task SetCreatedOnAsync(int productId, DateTime createdOn)
    {
        await DbContext.Product.Where(p => p.ProductId == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedOn, createdOn));
        DbContext.ChangeTracker.Clear();
    }

    private async Task SeedListingAsync(int productId, decimal quantity)
    {
        var user = new User
        {
            FirstName = "B60",
            LastName = "Seller",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"B60Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var variant = new ProductVariant { ProductId = productId, Name = "Default", IsActive = true };
        DbContext.ProductVariant.Add(variant);
        await DbContext.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 100m,
            Status = SellerProductStatus.Active
        };
        DbContext.SellerProduct.Add(listing);

        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "B60",
            RecipientLastName = "Shop",
            UserId = user.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, quantity));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }
}
