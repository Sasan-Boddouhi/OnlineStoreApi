using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.Brand;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Coupon;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.Shipping;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Services;

public class M710OperationalBlockersTests : BaseIntegrationTest
{
    private IOrderService OrderService => GetService<IOrderService>();
    private ICartService CartService => GetService<ICartService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private IShippingMethodService ShippingMethodService => GetService<IShippingMethodService>();
    private ICouponService CouponService => GetService<ICouponService>();
    private IBrandService BrandService => GetService<IBrandService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public M710OperationalBlockersTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId, string role = "Customer")
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private async Task<int> CreateCityAsync()
    {
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        return city.CityId;
    }

    private async Task<(User User, Customer Customer)> CreateBuyerAsync()
    {
        var user = new User
        {
            FirstName = "Buyer",
            LastName = "Test",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer
        {
            UserId = user.UserId,
            User = user
        };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (user, customer);
    }

    private async Task<(Seller Seller, Product Product, ProductVariant Variant, SellerProduct Listing)> SetupListingWithStockAsync(decimal price = 250m, decimal stock = 10m)
    {
        var user = new User
        {
            FirstName = "Seller",
            LastName = "Owner",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"Store-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var product = new Product
        {
            Name = $"Test Phone-{Guid.NewGuid():N}",
            Price = price,
            SubcategoryId = 1,
            ImageUrl = "https://example.com/phone.jpg"
        };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.ProductId,
            Name = "256GB Black",
            Sku = $"SKU-{Guid.NewGuid():N}"
        };
        DbContext.ProductVariant.Add(variant);
        await DbContext.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = price,
            Status = SellerProductStatus.Active
        };
        DbContext.SellerProduct.Add(listing);
        await DbContext.SaveChangesAsync();

        var cityId = await CreateCityAsync();
        var address = new Address
        {
            CityId = cityId,
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "12345",
            RecipientFirstName = "W",
            RecipientLastName = "H",
            UserId = user.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = $"WH-{Guid.NewGuid():N}",
            AddressId = address.AddressId,
            SellerId = seller.SellerId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        var inventory = new Inventory(listing.ListingId, warehouse.WarehouseId, stock);
        DbContext.Inventory.Add(inventory);
        await DbContext.SaveChangesAsync();

        return (seller, product, variant, listing);
    }

    // ==================== 1. Admin Order Blindspot ====================
    [Fact]
    public async Task AdminOrderBlindspot_AdminCanListOrders_AndInspectAnyCustomerOrder()
    {
        var (seller, product, variant, listing) = await SetupListingWithStockAsync(price: 100m, stock: 5m);
        var (buyer, _) = await CreateBuyerAsync();
        SetCurrentUser(buyer.UserId, "Customer");

        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1m });
        var order = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Customer Real Name",
            ShippingAddress = "Tehran, Street 5",
            ShippingPhoneNumber = "09129998877"
        });

        // 1a. Customer cannot see another user's order
        var (otherBuyer, _) = await CreateBuyerAsync();
        SetCurrentUser(otherBuyer.UserId, "Customer");
        var detailsForOther = await OrderService.GetOrderDetailsAsync(otherBuyer.UserId, order.OrderId);
        detailsForOther.Should().BeNull();

        // 1b. Admin CAN inspect the order
        var adminUser = new User
        {
            FirstName = "Admin",
            LastName = "User",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Employee
        };
        DbContext.User.Add(adminUser);
        await DbContext.SaveChangesAsync();

        SetCurrentUser(adminUser.UserId, "Admin");
        var adminDetails = await OrderService.GetOrderDetailsAsync(adminUser.UserId, order.OrderId);
        adminDetails.Should().NotBeNull();
        adminDetails!.ShippingFullName.Should().Be("Customer Real Name");

        // 1c. Admin can list all orders via GetAdminOrdersAsync
        var adminOrders = await OrderService.GetAdminOrdersAsync(OrderStatus.Pending, 1, 20);
        adminOrders.Items.Should().Contain(o => o.OrderId == order.OrderId);
    }

    // ==================== 2. Seller Fulfillment Blindspot ====================
    [Fact]
    public async Task SellerFulfillmentBlindspot_SellerCanViewOrderShippingDetails()
    {
        var (seller, product, variant, listing) = await SetupListingWithStockAsync(price: 150m, stock: 5m);
        var (buyer, _) = await CreateBuyerAsync();
        SetCurrentUser(buyer.UserId, "Customer");

        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 2m });
        var order = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Recipient For Seller",
            ShippingAddress = "Tehran, Valiasr St, No 12",
            ShippingPhoneNumber = "09123334455"
        });

        // Seller views their order details
        SetCurrentUser(seller.UserId, "Customer");
        var sellerOrderDetails = await DashboardService.GetSellerOrderDetailsAsync(order.OrderId);

        sellerOrderDetails.Should().NotBeNull();
        sellerOrderDetails.ShippingFullName.Should().Be("Recipient For Seller");
        sellerOrderDetails.ShippingAddress.Should().Be("Tehran, Valiasr St, No 12");
        sellerOrderDetails.ShippingPhoneNumber.Should().Be("09123334455");
        sellerOrderDetails.Items.Should().ContainSingle(i => i.ListingId == listing.ListingId && i.Quantity == 2m);
    }

    // ==================== 3. Payment Verify IDOR ====================
    [Fact]
    public async Task PaymentVerifyIDOR_DifferentCustomer_IsRejectedWithForbidden()
    {
        var (seller, product, variant, listing) = await SetupListingWithStockAsync(price: 200m, stock: 5m);
        var (buyer, _) = await CreateBuyerAsync();
        SetCurrentUser(buyer.UserId, "Customer");

        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1m });
        var order = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer IDOR",
            ShippingAddress = "Address",
            ShippingPhoneNumber = "09121112233"
        });

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);

        // Attacker tries to verify the payment
        var (attacker, _) = await CreateBuyerAsync();
        SetCurrentUser(attacker.UserId, "Customer");

        var act = () => PaymentService.VerifyAsync(initiation.Authority!);
        await act.Should().ThrowAsync<Application.Exceptions.ForbiddenException>();

        // Rightful buyer verifies successfully
        SetCurrentUser(buyer.UserId, "Customer");
        var verified = await PaymentService.VerifyAsync(initiation.Authority!);
        verified.State.Should().Be(PaymentState.Succeeded);
    }

    // ==================== 4. Missing Shipment Tracking ====================
    [Fact]
    public async Task ShipmentTracking_SellerShipsGroup_AndOrderTransitionsToShipped()
    {
        var (seller, product, variant, listing) = await SetupListingWithStockAsync(price: 300m, stock: 5m);
        var (buyer, _) = await CreateBuyerAsync();
        SetCurrentUser(buyer.UserId, "Customer");

        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1m });
        var order = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer Shipping",
            ShippingAddress = "Address",
            ShippingPhoneNumber = "09121112233"
        });

        // Pay for order so it moves to Processing
        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);
        await PaymentService.VerifyAsync(initiation.Authority!);

        var group = await DbContext.OrderSellerGroup.FirstAsync(g => g.OrderId == order.OrderId && g.SellerId == seller.SellerId);

        // Seller ships the package
        SetCurrentUser(seller.UserId, "Customer");
        var shippedDto = await DashboardService.ShipSellerGroupAsync(order.OrderId, group.OrderSellerGroupId, new ShipSellerGroupRequest
        {
            CarrierName = "Tipax",
            TrackingNumber = "TPX-987654321"
        });

        shippedDto.GroupStatus.Should().Be(SellerGroupStatus.Shipped.ToString());
        shippedDto.CarrierName.Should().Be("Tipax");
        shippedDto.TrackingNumber.Should().Be("TPX-987654321");
        shippedDto.ShippedAtUtc.Should().NotBeNull();

        // Main order should now be Shipped
        var refreshedOrder = await DbContext.Order.AsNoTracking().FirstAsync(o => o.OrderId == order.OrderId);
        refreshedOrder.Status.Should().Be(OrderStatus.Shipped);
    }

    // ==================== 5. Business Entities CRUD ====================
    [Fact]
    public async Task BusinessEntities_ShippingMethod_Coupon_Brand_CrudOperationsSucceed()
    {
        // 5a. ShippingMethod
        var shipping = await ShippingMethodService.CreateAsync(new CreateShippingMethodDto
        {
            Name = "پست پیشتاز",
            Slug = $"pishtaz-{Guid.NewGuid():N}",
            CarrierName = "IranPost",
            BaseCost = 35000m,
            CostPerKg = 5000m,
            EstimatedDays = 2
        });
        shipping.ShippingMethodId.Should().BeGreaterThan(0);
        var allShipping = await ShippingMethodService.GetAllAsync();
        allShipping.Should().Contain(s => s.ShippingMethodId == shipping.ShippingMethodId);

        // 5b. Coupon
        var coupon = await CouponService.CreateAsync(new CreateCouponDto
        {
            Code = $"WELCOME-{Guid.NewGuid():N}"[..12],
            DiscountPercentage = 15m,
            MaxDiscountAmount = 50000m,
            MinOrderAmount = 100000m
        });
        coupon.CouponId.Should().BeGreaterThan(0);
        var byCode = await CouponService.GetByCodeAsync(coupon.Code);
        byCode.Should().NotBeNull();
        byCode!.DiscountPercentage.Should().Be(15m);

        // 5c. Brand
        var brand = await BrandService.CreateAsync(new CreateBrandDto
        {
            Name = "Samsung",
            Slug = $"samsung-{Guid.NewGuid():N}",
            LogoUrl = "https://example.com/samsung.png"
        });
        brand.BrandId.Should().BeGreaterThan(0);
        var allBrands = await BrandService.GetAllAsync();
        allBrands.Should().Contain(b => b.BrandId == brand.BrandId);
    }
}
