using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using BusinessLogic.DTOs.Product;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 49 (F-29، F-30) — ارجاع‌های میان‌مستأجری و برند محصول:
///
///  • فروشنده نمی‌تواند انبارش را به آدرس کاربر دیگری وصل کند (403 ADDRESS_FORBIDDEN)؛
///    مدیر می‌تواند.
///  • موجودی یک Listing فقط در انبار فروشندهٔ همان Listing یا انبار پلتفرم ثبت می‌شود
///    (403 WAREHOUSE_FORBIDDEN)، حتی برای مدیر.
///  • ثبت موجودی دیگر استان/شهر/آدرس «Platform» نمی‌سازد؛ بدون انبار پلتفرم پاسخ
///    409 PLATFORM_WAREHOUSE_NOT_CONFIGURED است.
///  • BrandId محصول ذخیره می‌شود؛ برند ناموجود یا غیرفعال 422 INVALID_BRAND است.
/// </summary>
public class Phase15Batch49CrossTenantTests : ControllerIntegrationTestBase
{
    public Phase15Batch49CrossTenantTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== آدرس انبار ====================

    [Fact]
    public async Task Seller_CreateWarehouse_WithAnotherUsersAddress_IsForbidden()
    {
        var sellerA = await CreateSellerAsync();
        var victim = await CreateSellerAsync();
        var victimAddress = await CreateAddressAsync(victim.UserId);
        var name = "B49-" + Guid.NewGuid().ToString("N")[..8];

        var response = await SendAsync(HttpMethod.Post, "/api/v1/warehouses", sellerA.Token, new
        {
            Name = name,
            AddressId = victimAddress.AddressId
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "ADDRESS_FORBIDDEN");
        (await Db().Warehouse.AsNoTracking().AnyAsync(w => w.Name == name))
            .Should().BeFalse("a refused request must not leave a warehouse behind");
    }

    [Fact]
    public async Task Seller_CreateWarehouse_WithOwnAddress_Succeeds()
    {
        var seller = await CreateSellerAsync();
        var address = await CreateAddressAsync(seller.UserId);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/warehouses", seller.Token, new
        {
            Name = "B49-" + Guid.NewGuid().ToString("N")[..8],
            AddressId = address.AddressId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_CreateWarehouse_WithAnyAddress_Succeeds()
    {
        var seller = await CreateSellerAsync();
        var foreignAddress = await CreateAddressAsync((await CreateSellerAsync()).UserId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/warehouses", token, new
        {
            Name = "B49-" + Guid.NewGuid().ToString("N")[..8],
            AddressId = foreignAddress.AddressId,
            SellerId = seller.SellerId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Seller_UpdateWarehouse_ToAnotherUsersAddress_IsForbidden()
    {
        var seller = await CreateSellerAsync();
        var ownAddress = await CreateAddressAsync(seller.UserId);
        var warehouse = await CreateWarehouseAsync(seller.SellerId, ownAddress.AddressId);
        var victimAddress = await CreateAddressAsync((await CreateSellerAsync()).UserId);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/warehouses/{warehouse.WarehouseId}", seller.Token, new
        {
            WarehouseId = warehouse.WarehouseId,
            Name = warehouse.Name,
            AddressId = victimAddress.AddressId
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "ADDRESS_FORBIDDEN");
        (await Db().Warehouse.AsNoTracking().SingleAsync(w => w.WarehouseId == warehouse.WarehouseId))
            .AddressId.Should().Be(ownAddress.AddressId);
    }

    [Fact]
    public async Task Seller_UpdateWarehouse_ToOwnAddress_Succeeds()
    {
        var seller = await CreateSellerAsync();
        var first = await CreateAddressAsync(seller.UserId);
        var second = await CreateAddressAsync(seller.UserId);
        var warehouse = await CreateWarehouseAsync(seller.SellerId, first.AddressId);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/warehouses/{warehouse.WarehouseId}", seller.Token, new
        {
            WarehouseId = warehouse.WarehouseId,
            Name = warehouse.Name,
            AddressId = second.AddressId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await Db().Warehouse.AsNoTracking().SingleAsync(w => w.WarehouseId == warehouse.WarehouseId))
            .AddressId.Should().Be(second.AddressId);
    }

    [Fact]
    public async Task Seller_UpdateWarehouse_KeepingAddressAssignedByAdmin_Succeeds()
    {
        // انباری که مدیر با آدرس دیگری برای فروشنده ساخته، با تغییر نام قفل نمی‌شود:
        // فقط ارجاع «تازه» به آدرس دیگران رد می‌شود.
        var seller = await CreateSellerAsync();
        var foreignAddress = await CreateAddressAsync((await CreateSellerAsync()).UserId);
        var warehouse = await CreateWarehouseAsync(seller.SellerId, foreignAddress.AddressId);
        var newName = "B49-renamed-" + Guid.NewGuid().ToString("N")[..8];

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/warehouses/{warehouse.WarehouseId}", seller.Token, new
        {
            WarehouseId = warehouse.WarehouseId,
            Name = newName,
            AddressId = foreignAddress.AddressId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    // ==================== انبار مقصد موجودی ====================

    [Fact]
    public async Task Seller_AddStock_IntoAnotherSellersWarehouse_IsForbidden()
    {
        var seller = await CreateSellerAsync();
        var listingId = await CreateListingAsync(seller.SellerId);
        var other = await CreateSellerAsync();
        var otherWarehouse = await CreateWarehouseAsync(other.SellerId, (await CreateAddressAsync(other.UserId)).AddressId);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{listingId}/inventory", seller.Token, new
        {
            Quantity = 5,
            WarehouseId = otherWarehouse.WarehouseId
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "WAREHOUSE_FORBIDDEN");
        (await Db().Inventory.AsNoTracking().AnyAsync(i => i.ListingId == listingId))
            .Should().BeFalse("a refused request must not leave stock behind");
    }

    [Fact]
    public async Task Admin_AddStock_IntoWarehouseOfAnotherSeller_IsForbidden()
    {
        var seller = await CreateSellerAsync();
        var listingId = await CreateListingAsync(seller.SellerId);
        var other = await CreateSellerAsync();
        var otherWarehouse = await CreateWarehouseAsync(other.SellerId, (await CreateAddressAsync(other.UserId)).AddressId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/inventory/add-stock/{listingId}", token, new
        {
            Quantity = 5,
            WarehouseId = otherWarehouse.WarehouseId
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "WAREHOUSE_FORBIDDEN");
    }

    [Fact]
    public async Task Admin_CreateInventory_InWarehouseOfAnotherSeller_IsForbidden()
    {
        var seller = await CreateSellerAsync();
        var listingId = await CreateListingAsync(seller.SellerId);
        var other = await CreateSellerAsync();
        var otherWarehouse = await CreateWarehouseAsync(other.SellerId, (await CreateAddressAsync(other.UserId)).AddressId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/inventory", token, new
        {
            ListingId = listingId,
            WarehouseId = otherWarehouse.WarehouseId,
            InitialQuantity = 5
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "WAREHOUSE_FORBIDDEN");
    }

    [Fact]
    public async Task Seller_AddStock_IntoOwnWarehouse_Succeeds()
    {
        var seller = await CreateSellerAsync();
        var listingId = await CreateListingAsync(seller.SellerId);
        var warehouse = await CreateWarehouseAsync(seller.SellerId, (await CreateAddressAsync(seller.UserId)).AddressId);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{listingId}/inventory", seller.Token, new
        {
            Quantity = 5,
            WarehouseId = warehouse.WarehouseId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Seller_AddStock_IntoPlatformWarehouse_Succeeds()
    {
        var seller = await CreateSellerAsync();
        var listingId = await CreateListingAsync(seller.SellerId);
        var platform = await Db().Warehouse.AsNoTracking()
            .SingleAsync(w => w.SellerId == null && w.Name == TestDataSeed.PlatformWarehouseName);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{listingId}/inventory", seller.Token, new
        {
            Quantity = 5,
            WarehouseId = platform.WarehouseId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // ==================== انبار پیش‌فرض پلتفرم ====================

    [Fact]
    public async Task AddStock_WithoutWarehouse_UsesExistingPlatformWarehouse_AndCreatesNoGeography()
    {
        var seller = await CreateSellerAsync();
        var listingId = await CreateListingAsync(seller.SellerId);
        var provincesBefore = await Db().Province.AsNoTracking().CountAsync();
        var citiesBefore = await Db().City.AsNoTracking().CountAsync();
        var platform = await Db().Warehouse.AsNoTracking()
            .SingleAsync(w => w.SellerId == null && w.Name == TestDataSeed.PlatformWarehouseName);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{listingId}/inventory", seller.Token, new
        {
            Quantity = 5
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Db().Inventory.AsNoTracking().SingleAsync(i => i.ListingId == listingId))
            .WarehouseId.Should().Be(platform.WarehouseId);
        (await Db().Province.AsNoTracking().CountAsync()).Should().Be(provincesBefore);
        (await Db().City.AsNoTracking().CountAsync()).Should().Be(citiesBefore);
    }

    [Fact]
    public async Task AddStock_WithoutWarehouse_AndNoPlatformWarehouse_Returns409_AndCreatesNoPlatformGeography()
    {
        var seller = await CreateSellerAsync();
        var listingId = await CreateListingAsync(seller.SellerId);
        await RemovePlatformWarehousesAsync();

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/listings/{listingId}/inventory", seller.Token, new
        {
            Quantity = 5
        });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "PLATFORM_WAREHOUSE_NOT_CONFIGURED");

        var db = Db();
        (await db.Province.AsNoTracking().AnyAsync(p => p.ProvinceName == "Platform"))
            .Should().BeFalse("add-stock must never invent a \"Platform\" province");
        (await db.City.AsNoTracking().AnyAsync(c => c.CityName == "Platform"))
            .Should().BeFalse("add-stock must never invent a \"Platform\" city");
        (await db.Warehouse.AsNoTracking().AnyAsync(w => w.SellerId == null))
            .Should().BeFalse("add-stock must never invent a platform warehouse");
        (await db.Inventory.AsNoTracking().AnyAsync(i => i.ListingId == listingId)).Should().BeFalse();
    }

    // ==================== برند محصول ====================

    [Fact]
    public async Task CreateProduct_WithBrand_PersistsAndReturnsBrandId()
    {
        var brand = await CreateBrandAsync();
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/products", token, NewProduct(brand.BrandId));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<ProductDto>();
        created!.BrandId.Should().Be(brand.BrandId);
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == created.ProductId))
            .BrandId.Should().Be(brand.BrandId);
    }

    [Fact]
    public async Task CreateProduct_WithUnknownBrand_Returns422()
    {
        var token = await GetAdminTokenAsync();
        var body = NewProduct(999_999);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/products", token, body);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "INVALID_BRAND");
        (await Db().Product.AsNoTracking().AnyAsync(p => p.Name == (string)body["Name"]!)).Should().BeFalse();
    }

    [Fact]
    public async Task CreateProduct_WithInactiveBrand_Returns422()
    {
        var brand = await CreateBrandAsync(isActive: false);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/products", token, NewProduct(brand.BrandId));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "INVALID_BRAND");
    }

    [Fact]
    public async Task UpdateProduct_WithBrand_PersistsBrandId()
    {
        var brand = await CreateBrandAsync();
        var product = await CreateProductAsync(brandId: null);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, brand.BrandId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<ProductDto>())!.BrandId.Should().Be(brand.BrandId);
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().Be(brand.BrandId);
    }

    [Fact]
    public async Task UpdateProduct_WithUnknownBrand_Returns422_AndKeepsBrand()
    {
        var brand = await CreateBrandAsync();
        var product = await CreateProductAsync(brand.BrandId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, 999_999));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "INVALID_BRAND");
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().Be(brand.BrandId);
    }

    [Fact]
    public async Task UpdateProduct_WithoutBrandId_LeavesBrandUnchanged()
    {
        // UpdateProductDto فیلدهای nullable را ناهمگون تفسیر می‌کند (Unit/IsActive: «بدون تغییر»)،
        // پس null برای BrandId هم «بدون تغییر» است، نه «پاک کردن برند».
        var brand = await CreateBrandAsync();
        var product = await CreateProductAsync(brand.BrandId);
        var token = await GetAdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/products/{product.ProductId}", token,
            UpdateBody(product, brandId: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Db().Product.AsNoTracking().SingleAsync(p => p.ProductId == product.ProductId))
            .BrandId.Should().Be(brand.BrandId);
    }

    // ==================== ابزار ====================

    private sealed record SellerContext(int UserId, int SellerId, string Token);

    // خواندن‌ها همه AsNoTracking هستند تا وضعیت واقعی پایگاه داده را پس از درخواست HTTP ببینند.
    private AppDbContext Db() => GetService<AppDbContext>();

    private async Task<SellerContext> CreateSellerAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var response = await Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password = "Seller@123",
            FirstName = "Batch",
            LastName = "FortyNine",
            DeviceId = $"b49-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();

        var db = Db();
        var user = await db.User.SingleAsync(u => u.PhoneNumber == phone);
        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"B49-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        return new SellerContext(user.UserId, seller.SellerId, auth!.AccessToken);
    }

    private async Task<Address> CreateAddressAsync(int userId)
    {
        var db = Db();
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        db.City.Add(city);
        await db.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            Plaque = "7",
            Unit = "3",
            PostalCode = "1234567890",
            RecipientFirstName = "Rec",
            RecipientLastName = "Ipient",
            UserId = userId
        };
        db.Address.Add(address);
        await db.SaveChangesAsync();
        return address;
    }

    private async Task<Warehouse> CreateWarehouseAsync(int? sellerId, int addressId)
    {
        var db = Db();
        var warehouse = new Warehouse
        {
            Name = "B49-W-" + Guid.NewGuid().ToString("N")[..8],
            AddressId = addressId,
            SellerId = sellerId
        };
        db.Warehouse.Add(warehouse);
        await db.SaveChangesAsync();
        return warehouse;
    }

    private async Task<int> CreateListingAsync(int sellerId)
    {
        var db = Db();
        var product = new Product
        {
            Name = "B49-P-" + Guid.NewGuid().ToString("N")[..8],
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "Default" };
        db.ProductVariant.Add(variant);
        await db.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = sellerId,
            VariantId = variant.VariantId,
            Price = 100,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();
        return listing.ListingId;
    }

    private async Task RemovePlatformWarehousesAsync()
    {
        var db = Db();
        db.Warehouse.RemoveRange(await db.Warehouse.Where(w => w.SellerId == null).ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task<Brand> CreateBrandAsync(bool isActive = true)
    {
        var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var brand = new Brand { Name = "B49-Brand-" + suffix, Slug = "b49-brand-" + suffix, IsActive = isActive };
        db.Brand.Add(brand);
        await db.SaveChangesAsync();
        return brand;
    }

    private async Task<Product> CreateProductAsync(int? brandId)
    {
        var db = Db();
        var product = new Product
        {
            Name = "B49-P-" + Guid.NewGuid().ToString("N")[..8],
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece,
            BrandId = brandId
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private static Dictionary<string, object?> NewProduct(int? brandId) => new()
    {
        ["Name"] = "B49-New-" + Guid.NewGuid().ToString("N")[..8],
        ["Price"] = 1000,
        ["Unit"] = (int)UnitOfMeasurement.Piece,
        ["SubcategoryId"] = 1,
        ["BrandId"] = brandId
    };

    private static object UpdateBody(Product product, int? brandId) => new
    {
        ProductId = product.ProductId,
        Name = product.Name,
        Price = product.Price,
        SubcategoryId = product.SubcategoryId,
        Description = "Batch 49",
        BrandId = brandId
    };

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());
        return Client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
        json.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
