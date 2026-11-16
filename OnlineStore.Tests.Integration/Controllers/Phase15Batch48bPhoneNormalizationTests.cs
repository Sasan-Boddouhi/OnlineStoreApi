using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.User;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 48b — شمارهٔ موبایل ایران نرمال می‌شود، رد نمی‌شود (تصمیم مالک، جایگزین قاعدهٔ سخت Batch 48):
///
///  • ثبت‌نام با <c>+98…</c> پذیرفته می‌شود و فقط شکل استاندارد <c>09…</c> ذخیره و برگردانده می‌شود.
///  • ورود با هر قالب همان شماره (<c>09…</c>، <c>0098…</c>، ارقام فارسی) همان کاربر را پیدا می‌کند.
///  • همان شماره در قالبی دیگر تکراری است → 409 USER_PHONE_EXISTS (ثبت‌نام و ایجاد کاربر توسط ادمین).
///  • ایجاد/ویرایش کاربر توسط ادمین و شمارهٔ تحویل‌گیرندهٔ سفارش هم به شکل استاندارد ذخیره می‌شوند.
/// </summary>
public class Phase15Batch48bPhoneNormalizationTests : ControllerIntegrationTestBase
{
    private const decimal ItemPrice = 250m;

    public Phase15Batch48bPhoneNormalizationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== ثبت‌نام و ورود ====================

    [Fact]
    public async Task Register_WithPlus98_StoresAndReturnsCanonical_AndLoginWorksInOtherFormats()
    {
        var canonical = RandomCanonicalPhone();

        var register = await RegisterAsync("+98" + canonical[1..]);
        register.StatusCode.Should().Be(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());
        var result = await register.Content.ReadFromJsonAsync<AuthResultDto>();
        result!.User.PhoneNumber.Should().Be(canonical);

        (await StoredPhonesLikeAsync(canonical[1..])).Should().Equal(canonical);

        var loginCanonical = await LoginAsync(canonical);
        loginCanonical.StatusCode.Should().Be(HttpStatusCode.OK, await loginCanonical.Content.ReadAsStringAsync());

        var loginDoubleZero = await LoginAsync("0098" + canonical[1..]);
        loginDoubleZero.StatusCode.Should().Be(HttpStatusCode.OK, await loginDoubleZero.Content.ReadAsStringAsync());

        var loginPersianDigits = await LoginAsync(ToPersianDigits(canonical));
        loginPersianDigits.StatusCode.Should().Be(HttpStatusCode.OK, await loginPersianDigits.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_SameNumberInAnotherFormat_Returns409UserPhoneExists()
    {
        var canonical = RandomCanonicalPhone();
        var first = await RegisterAsync(canonical);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        await AssertProblemAsync(await RegisterAsync("+98 " + canonical[1..4] + " " + canonical[4..]), HttpStatusCode.Conflict, "USER_PHONE_EXISTS");
        await AssertProblemAsync(await RegisterAsync(canonical[1..]), HttpStatusCode.Conflict, "USER_PHONE_EXISTS");

        (await StoredPhonesLikeAsync(canonical[1..])).Should().Equal(canonical);
    }

    [Fact]
    public async Task Register_Landline_StillRejectedWith422()
    {
        var response = await RegisterAsync("02112345678");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
    }

    // ==================== ایجاد و ویرایش کاربر توسط ادمین ====================

    [Fact]
    public async Task AdminCreate_WithPlus98_StoresCanonical_AndSecondCreateInAnotherFormatReturns409()
    {
        var canonical = RandomCanonicalPhone();
        var token = await GetAdminTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/users", token, NewUserBody("+98" + canonical[1..]));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        (await create.Content.ReadFromJsonAsync<UserDto>())!.PhoneNumber.Should().Be(canonical);
        (await StoredPhonesLikeAsync(canonical[1..])).Should().Equal(canonical);

        var duplicate = await SendAsync(HttpMethod.Post, "/api/v1/users", token, NewUserBody("0098" + canonical[1..]));
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "USER_PHONE_EXISTS");

        (await StoredPhonesLikeAsync(canonical[1..])).Should().Equal(canonical);
    }

    [Fact]
    public async Task AdminUpdate_WithPlus98_StoresCanonical_AndOtherUsersNumberInAnotherFormatReturns409()
    {
        var token = await GetAdminTokenAsync();
        var target = await CreateCustomerDirectlyAsync(RandomCanonicalPhone());
        var other = await CreateCustomerDirectlyAsync(RandomCanonicalPhone());
        var newCanonical = RandomCanonicalPhone();

        var update = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target.UserId}", token, new
        {
            UserId = target.UserId,
            PhoneNumber = "+98" + newCanonical[1..],
            FirstName = target.FirstName,
            LastName = target.LastName
        });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent, await update.Content.ReadAsStringAsync());
        (await LoadUserAsync(target.UserId)).PhoneNumber.Should().Be(newCanonical);

        var duplicate = await SendAsync(HttpMethod.Put, $"/api/v1/users/{target.UserId}", token, new
        {
            UserId = target.UserId,
            PhoneNumber = "0098" + other.PhoneNumber[1..],
            FirstName = target.FirstName,
            LastName = target.LastName
        });
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "USER_PHONE_EXISTS");
        (await LoadUserAsync(target.UserId)).PhoneNumber.Should().Be(newCanonical);
    }

    // ==================== شمارهٔ تحویل‌گیرندهٔ سفارش ====================

    [Fact]
    public async Task Checkout_WithPlus98RecipientPhone_StoresCanonicalOnOrder()
    {
        var methodId = await CreateShippingMethodAsync();
        var (token, userId) = await RegisterCustomerAsync();
        var listingId = await SeedListingAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var add = await Client.PostAsJsonAsync("/api/v1/carts/items", new { ListingId = listingId, Quantity = 1 });
        add.StatusCode.Should().Be(HttpStatusCode.OK, await add.Content.ReadAsStringAsync());

        var checkout = await Client.PostAsJsonAsync("/api/v1/carts/checkout", new
        {
            ShippingFullName = "B48b Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "+98 912 345 6789",
            ShippingMethodId = methodId
        });
        checkout.StatusCode.Should().Be(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());
        (await checkout.Content.ReadFromJsonAsync<OrderDto>())!.ShippingPhoneNumber.Should().Be("09123456789");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Order.AsNoTracking().SingleAsync(o => o.Customer.UserId == userId);
        order.ShippingPhoneNumber.Should().Be("09123456789");
    }

    // ==================== helpers ====================

    /// <summary>شمارهٔ استاندارد تصادفی؛ پیش‌شمارهٔ 0917 با شماره‌های ثابت seed برخورد ندارد.</summary>
    private static string RandomCanonicalPhone() => $"0917{Random.Shared.Next(1000000, 9999999):D7}";

    private static string ToPersianDigits(string ascii)
        => new(ascii.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());

    private static object NewUserBody(string phone) => new
    {
        PhoneNumber = phone,
        Password = "Strong@123",
        FirstName = "Batch",
        LastName = "FortyEightB",
        DateOfBirth = "1370/01/01"
    };

    private Task<HttpResponseMessage> RegisterAsync(string phone)
        => Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Batch",
            LastName = "FortyEightB",
            DeviceId = $"b48b-{Guid.NewGuid():N}"
        });

    private Task<HttpResponseMessage> LoginAsync(string phone)
        => Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            DeviceId = $"b48b-{Guid.NewGuid():N}"
        });

    private async Task<(string Token, int UserId)> RegisterCustomerAsync()
    {
        var phone = RandomCanonicalPhone();
        var response = await RegisterAsync(phone);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        return (result!.AccessToken, result.User.UserId);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return Client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    /// <summary>همهٔ شماره‌های ذخیره‌شده‌ای که ۱۰ رقم آخرشان این است؛ قالب غیراستاندارد هم دیده می‌شود.</summary>
    private async Task<List<string>> StoredPhonesLikeAsync(string lastTenDigits)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var all = await db.User.AsNoTracking().Select(u => u.PhoneNumber).ToListAsync();
        return all.Where(p => p.EndsWith(lastTenDigits, StringComparison.Ordinal)).ToList();
    }

    private async Task<User> LoadUserAsync(int userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.User.AsNoTracking().FirstAsync(u => u.UserId == userId);
    }

    private async Task<User> CreateCustomerDirectlyAsync(string phone)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User
        {
            FirstName = "B48b",
            LastName = "Customer",
            PhoneNumber = phone,
            PasswordHash = "not-used",
            UserType = UserType.Customer,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();

        db.Customer.Add(new Customer { UserId = user.UserId });
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<int> CreateShippingMethodAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var method = new ShippingMethod
        {
            Name = $"Post-{Guid.NewGuid():N}",
            Slug = $"post-{Guid.NewGuid():N}",
            BaseCost = 45m,
            IsActive = true
        };
        db.ShippingMethod.Add(method);
        await db.SaveChangesAsync();
        return method.ShippingMethodId;
    }

    private async Task<int> SeedListingAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sellerUser = await CreateCustomerDirectlyAsync(RandomCanonicalPhone());

        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B48bShop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B48b-{Guid.NewGuid():N}",
            Price = ItemPrice,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece,
            IsActive = true
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "Default", IsActive = true };
        db.ProductVariant.Add(variant);
        await db.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = ItemPrice,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);

        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        db.City.Add(city);
        await db.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "B48b",
            RecipientLastName = "Shop",
            UserId = sellerUser.UserId
        };
        db.Address.Add(address);
        await db.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        db.Warehouse.Add(warehouse);
        await db.SaveChangesAsync();

        db.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, 50m));
        await db.SaveChangesAsync();

        return listing.ListingId;
    }
}
