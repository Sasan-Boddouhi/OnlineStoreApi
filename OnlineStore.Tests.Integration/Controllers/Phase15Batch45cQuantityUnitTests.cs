using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 45c (BLOCKER، بازبینی مالک پس از 45b): مقدار هر قلم تابع واحد کالاست.
///  • کالای عددی (Piece): فقط عدد صحیح ۱ یا بیشتر؛ ۰٫۵ گوشی با نصف قیمت دیگر ممکن نیست.
///  • کالای وزنی/حجمی/طولی: اعشار مجاز، حداکثر دو رقم؛ بیشتر از دو رقم رد می‌شود و هرگز بی‌صدا گرد نمی‌شود.
/// همهٔ ورودی‌ها (افزودن به سبد، تغییر تعداد، ادغام سبد مهمان، checkout، افزودن قلم سفارش، رزرو و آزادسازی موجودی)
/// با 422 <c>QUANTITY_INVALID_FOR_UNIT</c> رد می‌کنند. پیش از 45c همهٔ این مسیرها مقدار را می‌پذیرفتند.
/// </summary>
public class Phase15Batch45cQuantityUnitTests : ControllerIntegrationTestBase
{
    private const string Code = "QUANTITY_INVALID_FOR_UNIT";
    private const decimal Price = 12_345m;
    private const decimal Stock = 100m;

    private AppDbContext Db => GetService<AppDbContext>();

    public Phase15Batch45cQuantityUnitTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== سبد (HTTP) ====================

    [Fact]
    public async Task AddToCart_HalfAPhone_Returns422_AndNothingIsStored()
    {
        var (token, _) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Piece);
        Authenticate(token);

        var response = await Client.PostAsJsonAsync("/api/v1/carts/items", new { ListingId = listingId, Quantity = 0.5m });

        await ShouldBeRejectedAsync(response);
        (await Db.CartItem.AsNoTracking().AnyAsync(i => i.ListingId == listingId)).Should().BeFalse();
    }

    [Fact]
    public async Task AddToCart_OneAndAQuarterKilo_IsAccepted_WithAWholeRialLineTotal_AndChecksOutWithTheSameTotal()
    {
        var (token, _) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Kilogram);
        var shippingMethodId = await CreateShippingMethodAsync();
        Authenticate(token);

        var response = await Client.PostAsJsonAsync("/api/v1/carts/items", new { ListingId = listingId, Quantity = 1.25m });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cart = await response.Content.ReadFromJsonAsync<CartDto>();
        var line = cart!.Items.Single();
        line.Quantity.Should().Be(1.25m);
        line.LineTotal.Should().Be(15_431m, "1.25 × 12,345 = 15,431.25 is rounded to a whole Rial (N6)");

        var checkout = await Client.PostAsJsonAsync("/api/v1/carts/checkout", CheckoutBody(shippingMethodId));
        checkout.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await Db.OrderItem.AsNoTracking().SingleAsync(i => i.ListingId == listingId);
        (item.Quantity, item.TotalPrice).Should().Be((1.25m, 15_431m));
        (await StockAsync(listingId)).Should().Be(Stock - 1.25m);
    }

    [Fact]
    public async Task AddToCart_ThreeDecimalKilos_Returns422_AndIsNeverRounded()
    {
        var (token, _) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Kilogram);
        Authenticate(token);

        var response = await Client.PostAsJsonAsync("/api/v1/carts/items", new { ListingId = listingId, Quantity = 1.255m });

        await ShouldBeRejectedAsync(response);
        (await Db.CartItem.AsNoTracking().AnyAsync(i => i.ListingId == listingId)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateQuantity_FractionalPhone_Returns422_AndKeepsTheQuantity()
    {
        var (token, _) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Piece);
        Authenticate(token);
        (await Client.PostAsJsonAsync("/api/v1/carts/items", new { ListingId = listingId, Quantity = 2m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.PutAsJsonAsync($"/api/v1/carts/items/{listingId}", new { Quantity = 1.5m });

        await ShouldBeRejectedAsync(response);
        (await Db.CartItem.AsNoTracking().SingleAsync(i => i.ListingId == listingId)).Quantity.Should().Be(2m);
    }

    [Fact]
    public async Task UpdateQuantity_ThreeDecimalKilos_Returns422_AndKeepsTheQuantity()
    {
        var (token, _) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Kilogram);
        Authenticate(token);
        (await Client.PostAsJsonAsync("/api/v1/carts/items", new { ListingId = listingId, Quantity = 0.5m }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.PutAsJsonAsync($"/api/v1/carts/items/{listingId}", new { Quantity = 0.125m });

        await ShouldBeRejectedAsync(response);
        (await Db.CartItem.AsNoTracking().SingleAsync(i => i.ListingId == listingId)).Quantity.Should().Be(0.5m);
    }

    /// <summary>سبد مهمانی که پیش از 45c نیم گوشی گرفته بود، در ادغام پس از ورود رد می‌شود و هیچ‌چیز جابه‌جا نمی‌شود.</summary>
    [Fact]
    public async Task MergeGuestCart_WithHalfAPhone_Returns422_AndLeavesBothCartsUntouched()
    {
        var (token, userId) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Piece);
        var sessionId = Guid.NewGuid().ToString();
        await SeedCartAsync(new Cart { SessionId = sessionId }, listingId, 0.5m);
        Authenticate(token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/carts/merge");
        request.Headers.Add("X-Cart-Session", sessionId);
        var response = await Client.SendAsync(request);

        await ShouldBeRejectedAsync(response);
        var guestItem = await Db.CartItem.AsNoTracking().Include(i => i.Cart).SingleAsync(i => i.ListingId == listingId);
        guestItem.Cart.SessionId.Should().Be(sessionId);
        (await Db.Cart.AsNoTracking().Where(c => c.UserId == userId).SelectMany(c => c.Items).AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task MergeGuestCart_WithOneAndAQuarterKilo_IsAccepted()
    {
        var (token, _) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Kilogram);
        var sessionId = Guid.NewGuid().ToString();
        await SeedCartAsync(new Cart { SessionId = sessionId }, listingId, 1.25m);
        Authenticate(token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/carts/merge");
        request.Headers.Add("X-Cart-Session", sessionId);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cart = await response.Content.ReadFromJsonAsync<CartDto>();
        cart!.Items.Single().LineTotal.Should().Be(15_431m);
        (await Db.Cart.AsNoTracking().AnyAsync(c => c.SessionId == sessionId)).Should().BeFalse();
    }

    /// <summary>سبدی که پیش از 45c نیم گوشی گرفته بود، checkout نمی‌شود؛ نه سفارشی ساخته می‌شود و نه موجودی کم می‌شود.</summary>
    [Fact]
    public async Task Checkout_CartWithHalfAPhone_Returns422_NoOrderAndNoStockTaken()
    {
        var (token, userId) = await RegisterBuyerAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Piece);
        var shippingMethodId = await CreateShippingMethodAsync();
        await SeedCartAsync(new Cart { UserId = userId }, listingId, 0.5m);
        Authenticate(token);

        var response = await Client.PostAsJsonAsync("/api/v1/carts/checkout", CheckoutBody(shippingMethodId));

        await ShouldBeRejectedAsync(response);
        (await Db.OrderItem.AsNoTracking().AnyAsync(i => i.ListingId == listingId)).Should().BeFalse();
        (await StockAsync(listingId)).Should().Be(Stock);
    }

    // ==================== قلم سفارش و موجودی (سرویس) ====================

    [Fact]
    public async Task AddOrderItem_HalfAPhone_IsRejected_AndNoStockIsTaken()
    {
        var orderId = await CreateEmptyOrderAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Piece);
        var orders = GetService<IOrderService>();

        var act = () => orders.AddItemToOrderAsync(orderId, new AddOrderItemDto { ListingId = listingId, Quantity = 0.5m });

        await ShouldBeRejectedAsync(act);
        (await Db.OrderItem.AsNoTracking().AnyAsync(i => i.OrderId == orderId)).Should().BeFalse();
        (await StockAsync(listingId)).Should().Be(Stock);
    }

    [Fact]
    public async Task AddOrderItems_ThreeDecimalKilos_IsRejected()
    {
        var orderId = await CreateEmptyOrderAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Kilogram);
        var orders = GetService<IOrderService>();

        var act = () => orders.AddItemsToOrderAsync(orderId, new[] { new AddOrderItemDto { ListingId = listingId, Quantity = 1.255m } });

        await ShouldBeRejectedAsync(act);
        (await Db.OrderItem.AsNoTracking().AnyAsync(i => i.OrderId == orderId)).Should().BeFalse();
    }

    [Fact]
    public async Task AddOrderItem_OneAndAQuarterKilo_IsAccepted_WithAWholeRialTotal()
    {
        var orderId = await CreateEmptyOrderAsync();
        var listingId = await CreateListingAsync(UnitOfMeasurement.Kilogram);
        var orders = GetService<IOrderService>();

        await orders.AddItemToOrderAsync(orderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1.25m });

        var item = await Db.OrderItem.AsNoTracking().SingleAsync(i => i.OrderId == orderId);
        (item.Quantity, item.TotalPrice).Should().Be((1.25m, 15_431m));
        (await StockAsync(listingId)).Should().Be(Stock - 1.25m);
    }

    [Theory]
    [InlineData(UnitOfMeasurement.Piece, "0.5")]
    [InlineData(UnitOfMeasurement.Kilogram, "1.255")]
    public async Task ReserveAndRelease_ApplyTheSameRule_AndLeaveTheStockUntouched(UnitOfMeasurement unit, string quantityText)
    {
        var quantity = decimal.Parse(quantityText, System.Globalization.CultureInfo.InvariantCulture);
        var listingId = await CreateListingAsync(unit);
        var inventory = GetService<IInventoryService>();

        await ShouldBeRejectedAsync(() => inventory.ReserveAsync(listingId, quantity));
        await ShouldBeRejectedAsync(() => inventory.ReleaseAsync(listingId, quantity));

        await GetService<IUnitOfWork>().SaveChangesAsync();
        (await StockAsync(listingId)).Should().Be(Stock);
    }

    [Fact]
    public async Task ReserveAndRelease_OneAndAQuarterKilo_AreAccepted()
    {
        var listingId = await CreateListingAsync(UnitOfMeasurement.Kilogram);
        var inventory = GetService<IInventoryService>();
        var unitOfWork = GetService<IUnitOfWork>();

        await inventory.ReserveAsync(listingId, 1.25m);
        await unitOfWork.SaveChangesAsync();
        (await StockAsync(listingId)).Should().Be(Stock - 1.25m);

        await inventory.ReleaseAsync(listingId, 1.25m);
        await unitOfWork.SaveChangesAsync();
        (await StockAsync(listingId)).Should().Be(Stock);
    }

    // ==================== ابزارها ====================

    private static async Task ShouldBeRejectedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("code").GetString().Should().Be(Code);
    }

    private static async Task ShouldBeRejectedAsync(Func<Task> act)
    {
        var ex = await act.Should().ThrowAsync<AppException>();
        ex.Which.Code.Should().Be(Code);
        ex.Which.StatusCode.Should().Be(422);
    }

    private void Authenticate(string token)
        => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static object CheckoutBody(int shippingMethodId) => new
    {
        ShippingFullName = "B45c Buyer",
        ShippingAddress = "Tehran",
        ShippingPhoneNumber = "09120000000",
        ShippingMethodId = shippingMethodId
    };

    private async Task<decimal> StockAsync(int listingId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // SQLite cannot SUM decimals; the rows are summed on the client.
        return (await db.Inventory.AsNoTracking().Where(i => i.ListingId == listingId).Select(i => i.Quantity).ToListAsync()).Sum();
    }

    private async Task<(string Token, int UserId)> RegisterBuyerAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var response = await Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "B45c",
            LastName = "Buyer",
            DeviceId = $"device-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();
        var user = await Db.User.AsNoTracking().FirstAsync(u => u.PhoneNumber == phone);
        return (auth!.AccessToken, user.UserId);
    }

    private async Task<int> CreateEmptyOrderAsync()
    {
        var user = new User
        {
            FirstName = "B45c",
            LastName = "Customer",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        Db.User.Add(user);
        await Db.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        Db.Customer.Add(customer);
        await Db.SaveChangesAsync();

        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = user.UserId;
        fake.CurrentUserRole = "Customer";

        var order = await GetService<IOrderService>()
            .CreateEmptyOrderAsync(customer.CustomerId, "B45c Buyer", "Tehran", "09120000000");
        return order.OrderId;
    }

    private async Task<int> CreateShippingMethodAsync()
    {
        var shipping = new ShippingMethod { Name = "Post", Slug = $"b45c-{Guid.NewGuid():N}"[..20], BaseCost = 25_000m, IsActive = true };
        Db.ShippingMethod.Add(shipping);
        await Db.SaveChangesAsync();
        return shipping.ShippingMethodId;
    }

    private async Task SeedCartAsync(Cart cart, int listingId, decimal quantity)
    {
        var listing = await Db.SellerProduct.AsNoTracking().FirstAsync(l => l.ListingId == listingId);
        cart.Items.Add(new CartItem
        {
            ListingId = listingId,
            VariantId = listing.VariantId,
            Quantity = quantity,
            UnitPrice = listing.Price,
            AddedAt = DateTime.UtcNow
        });
        Db.Cart.Add(cart);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    private async Task<int> CreateListingAsync(UnitOfMeasurement unit)
    {
        var sellerUser = new User
        {
            FirstName = "B45c",
            LastName = "Seller",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        Db.User.Add(sellerUser);
        await Db.SaveChangesAsync();

        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B45c-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        Db.Seller.Add(seller);
        await Db.SaveChangesAsync();

        var product = new Product
        {
            Name = $"B45c-{unit}-{Guid.NewGuid():N}",
            Price = Price,
            SubcategoryId = 1,
            Unit = unit,
            IsActive = true
        };
        Db.Product.Add(product);
        await Db.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "Default", IsActive = true };
        Db.ProductVariant.Add(variant);
        await Db.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = Price,
            Status = SellerProductStatus.Active
        };
        Db.SellerProduct.Add(listing);

        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        Db.Province.Add(province);
        await Db.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        Db.City.Add(city);
        await Db.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "B45c",
            RecipientLastName = "Shop",
            UserId = sellerUser.UserId
        };
        Db.Address.Add(address);
        await Db.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        Db.Warehouse.Add(warehouse);
        await Db.SaveChangesAsync();

        Db.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, Stock));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        return listing.ListingId;
    }
}
