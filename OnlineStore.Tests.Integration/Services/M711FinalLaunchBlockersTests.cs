using System.Net;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Coupon;
using BusinessLogic.DTOs.Dashboard;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.DTOs.Shipping;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Services;

public class M711FinalLaunchBlockersTests : BaseIntegrationTest
{
    private IAuthService AuthService => GetService<IAuthService>();
    private ICartService CartService => GetService<ICartService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private ICouponService CouponService => GetService<ICouponService>();
    private IShippingMethodService ShippingMethodService => GetService<IShippingMethodService>();
    private ICityService CityService => GetService<ICityService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public M711FinalLaunchBlockersTests(IntegrationTestFactory<Program> factory) : base(factory) { }

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

    private async Task<(Seller Seller, Product Product, ProductVariant Variant, SellerProduct Listing)> SetupListingWithStockAsync(decimal price = 200m, decimal stock = 10m)
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
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var product = new Product
        {
            Name = $"P-{Guid.NewGuid():N}",
            Price = price,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece,
            IsActive = true
        };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.ProductId,
            Name = "Default",
            IsActive = true
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
            Name = $"W-{Guid.NewGuid():N}",
            SellerId = seller.SellerId,
            AddressId = address.AddressId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        var inventory = new Inventory(listing.ListingId, warehouse.WarehouseId, stock);
        DbContext.Inventory.Add(inventory);
        await DbContext.SaveChangesAsync();

        return (seller, product, variant, listing);
    }

    // 1. Auto-provision Customer profile on registration
    [Fact]
    public async Task Registration_AutoCreatesCustomerProfile()
    {
        var phone = $"09{Random.Shared.Next(100000000, 999999999)}";
        var result = await AuthService.RegisterAsync(new RegisterDto
        {
            FirstName = "Auto",
            LastName = "Customer",
            PhoneNumber = phone,
            Password = "Password123!",
            DeviceId = "device1"
        });

        result.Should().NotBeNull();
        result.User.UserId.Should().BeGreaterThan(0);

        var customer = await DbContext.Customer
            .FirstOrDefaultAsync(c => c.UserId == result.User.UserId);

        customer.Should().NotBeNull();
        customer!.CustomerId.Should().BeGreaterThan(0);
    }

    // 2. Checkout fallback auto-provisions Customer for legacy users
    [Fact]
    public async Task Checkout_LegacyUserWithoutCustomer_AutoProvisionsCustomerAndSucceeds()
    {
        var user = new User
        {
            FirstName = "Legacy",
            LastName = "User",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        // User has NO customer record
        (await DbContext.Customer.AnyAsync(c => c.UserId == user.UserId)).Should().BeFalse();

        var (_, _, _, listing) = await SetupListingWithStockAsync(150m, 5m);

        SetCurrentUser(user.UserId, "Customer");
        await CartService.AddItemAsync(user.UserId, null, new AddCartItemDto
        {
            ListingId = listing.ListingId,
            Quantity = 1
        });

        var order = await CartService.CheckoutAsync(user.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Legacy User",
            ShippingAddress = "Tehran Main Street",
            ShippingPhoneNumber = "09123456789"
        });

        order.Should().NotBeNull();
        order.TotalAmount.Should().Be(150m);

        var customer = await DbContext.Customer.FirstOrDefaultAsync(c => c.UserId == user.UserId);
        customer.Should().NotBeNull();
    }

    // 3. Prevent Seller Privilege Escalation & Admin Update
    [Fact]
    public async Task Seller_CannotUpdateStatusOrCommission_ViaProfileDto_AdminCanUpdate()
    {
        var user = new User
        {
            FirstName = "Rogue",
            LastName = "Seller",
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
            Status = SellerStatus.Pending,
            CommissionRate = 15m,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        // Seller updates profile via UpdateSellerProfileDto
        var updatedProfile = await SellerService.UpdateProfileAsync(seller.SellerId, new UpdateSellerProfileDto
        {
            StoreName = "Renamed Store",
            Description = "Updated description"
        });

        updatedProfile.StoreName.Should().Be("Renamed Store");
        var reloaded = await DbContext.Seller.FirstAsync(s => s.SellerId == seller.SellerId);
        reloaded.Status.Should().Be(SellerStatus.Pending); // Status untouched!
        reloaded.CommissionRate.Should().Be(15m); // Commission untouched!

        // Admin updates via UpdateSellerDto
        var adminUpdated = await SellerService.UpdateAsync(seller.SellerId, new UpdateSellerDto
        {
            StoreName = "Renamed Store",
            Status = SellerStatus.Approved,
            CommissionRate = 8m
        });

        adminUpdated.Status.Should().Be(SellerStatus.Approved.ToString());
        reloaded = await DbContext.Seller.FirstAsync(s => s.SellerId == seller.SellerId);
        reloaded.Status.Should().Be(SellerStatus.Approved);
        reloaded.CommissionRate.Should().Be(8m);
    }

    // 4. Cascade Seller Suspension to Listings
    [Fact]
    public async Task SellerSuspension_CascadesToInactiveListings()
    {
        var (seller, _, _, listing) = await SetupListingWithStockAsync(100m, 10m);

        listing.Status.Should().Be(SellerProductStatus.Active);

        // Admin suspends seller
        await SellerService.UpdateAsync(seller.SellerId, new UpdateSellerDto
        {
            StoreName = seller.StoreName,
            Status = SellerStatus.Suspended,
            IsActive = false
        });

        var reloadedListing = await DbContext.SellerProduct.FirstAsync(l => l.ListingId == listing.ListingId);
        reloadedListing.Status.Should().Be(SellerProductStatus.Inactive);
    }

    // 5. Suspended User Refresh Token is Rejected
    [Fact]
    public async Task RefreshToken_WhenUserIsInactive_ThrowsUnauthorizedException()
    {
        var phone = $"09{Random.Shared.Next(100000000, 999999999)}";
        var auth = await AuthService.RegisterAsync(new RegisterDto
        {
            FirstName = "Inactive",
            LastName = "User",
            PhoneNumber = phone,
            Password = "Password123!",
            DeviceId = "dev-inactive"
        });

        // Deactivate user
        var user = await DbContext.User.FirstAsync(u => u.UserId == auth.User.UserId);
        user.IsActive = false;
        await DbContext.SaveChangesAsync();

        var act = () => AuthService.RefreshTokenAsync(auth.RefreshToken);
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*غیرفعال*");
    }

    // 6. Listing Creation IDOR is Plugged
    [Fact]
    public async Task CreateListing_NonSellerCannotSupplyAnotherSellerId()
    {
        var (seller, product, variant, _) = await SetupListingWithStockAsync();

        var nonSellerUser = new User
        {
            FirstName = "Hacker",
            LastName = "Customer",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(nonSellerUser);
        await DbContext.SaveChangesAsync();

        SetCurrentUser(nonSellerUser.UserId, "Customer");

        var act = () => SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId, // Trying to inject another seller's id
            VariantId = variant.VariantId,
            Price = 50m
        });

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*فروشنده نیست*");
    }

    // 7. Independent Seller Group Delivery & Immediate Payout
    [Fact]
    public async Task IndependentSellerGroupDelivery_CreatesPayoutImmediately_AndCompletesOrderWhenAllDelivered()
    {
        var (sellerA, _, _, listingA) = await SetupListingWithStockAsync(100m, 10m);
        var (sellerB, _, _, listingB) = await SetupListingWithStockAsync(200m, 10m);

        var (buyerUser, buyerCustomer) = (new User
        {
            FirstName = "Buyer",
            LastName = "Multi",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        }, (Customer)null!);

        DbContext.User.Add(buyerUser);
        await DbContext.SaveChangesAsync();
        buyerCustomer = new Customer { UserId = buyerUser.UserId };
        DbContext.Customer.Add(buyerCustomer);
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyerUser.UserId, "Customer");
        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = listingA.ListingId, Quantity = 1 });
        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = listingB.ListingId, Quantity = 1 });

        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer Multi",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09121112233"
        });

        // Confirm order
        SetCurrentUser(buyerUser.UserId, "Customer");
        await OrderService.ConfirmOrderAsync(order.OrderId);

        var groups = await DbContext.OrderSellerGroup
            .Where(g => g.OrderId == order.OrderId)
            .OrderBy(g => g.SellerId)
            .ToListAsync();

        groups.Should().HaveCount(2);
        var groupA = groups.First(g => g.SellerId == sellerA.SellerId);
        var groupB = groups.First(g => g.SellerId == sellerB.SellerId);

        // Seller A ships
        SetCurrentUser(sellerA.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(order.OrderId, groupA.OrderSellerGroupId, new ShipSellerGroupRequest
        {
            CarrierName = "Tipax",
            TrackingNumber = "TRACK-A"
        });

        // Seller A delivers independently!
        var deliveredGroupA = await DashboardService.DeliverSellerGroupAsync(order.OrderId, groupA.OrderSellerGroupId);
        deliveredGroupA.GroupStatus.Should().Be(SellerGroupStatus.Delivered.ToString());

        // Payout for Seller A is created immediately!
        var payoutA = await DbContext.SellerPayout
            .FirstOrDefaultAsync(p => p.OrderSellerGroupId == groupA.OrderSellerGroupId);
        payoutA.Should().NotBeNull();
        payoutA!.SellerId.Should().Be(sellerA.SellerId);
        payoutA.Status.Should().Be(PayoutStatus.Pending);

        // Order is NOT delivered yet because Seller B hasn't delivered!
        var orderMid = await DbContext.Order.FirstAsync(o => o.OrderId == order.OrderId);
        orderMid.Status.Should().NotBe(OrderStatus.Delivered);

        // Now Seller B ships & delivers
        SetCurrentUser(sellerB.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(order.OrderId, groupB.OrderSellerGroupId, new ShipSellerGroupRequest
        {
            CarrierName = "Post",
            TrackingNumber = "TRACK-B"
        });
        await DashboardService.DeliverSellerGroupAsync(order.OrderId, groupB.OrderSellerGroupId);

        // Now whole order is Delivered!
        var orderFinal = await DbContext.Order.FirstAsync(o => o.OrderId == order.OrderId);
        orderFinal.Status.Should().Be(OrderStatus.Delivered);

        // Payout B also exists!
        var payoutB = await DbContext.SellerPayout
            .FirstOrDefaultAsync(p => p.OrderSellerGroupId == groupB.OrderSellerGroupId);
        payoutB.Should().NotBeNull();
        payoutB!.SellerId.Should().Be(sellerB.SellerId);
    }

    // 8. Checkout with Coupon and ShippingMethod
    [Fact]
    public async Task Checkout_WithCouponAndShippingMethod_AppliesDiscountAndShippingCost()
    {
        var (seller, _, _, listing) = await SetupListingWithStockAsync(price: 1000m, stock: 10m);

        // Create active coupon (10% off, max 200, min order 500)
        var coupon = await CouponService.CreateAsync(new CreateCouponDto
        {
            Code = $"OFF10-{Guid.NewGuid():N}",
            DiscountPercentage = 10m,
            MinOrderAmount = 500m,
            MaxDiscountAmount = 200m,
            IsActive = true
        });

        // Create active shipping method (BaseCost = 50)
        var shipping = await ShippingMethodService.CreateAsync(new CreateShippingMethodDto
        {
            Name = "Express Delivery",
            Slug = $"express-{Guid.NewGuid():N}",
            BaseCost = 50m,
            CostPerKg = 0m,
            EstimatedDays = 2,
            IsActive = true
        });

        var buyerUser = new User
        {
            FirstName = "Coupon",
            LastName = "Buyer",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(buyerUser);
        await DbContext.SaveChangesAsync();
        var buyerCustomer = new Customer { UserId = buyerUser.UserId };
        DbContext.Customer.Add(buyerCustomer);
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyerUser.UserId, "Customer");
        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto
        {
            ListingId = listing.ListingId,
            Quantity = 1
        });

        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Coupon Buyer",
            ShippingAddress = "Tehran Azadi",
            ShippingPhoneNumber = "09121234567",
            CouponCode = coupon.Code,
            ShippingMethodId = shipping.ShippingMethodId
        });

        // Items total = 1000. Discount = 10% of 1000 = 100. Shipping = 50. Total = 1000 - 100 + 50 = 950.
        order.Should().NotBeNull();
        order.DiscountAmount.Should().Be(100m);
        order.ShippingCost.Should().Be(50m);
        order.TotalAmount.Should().Be(950m);

        var savedOrder = await DbContext.Order.FirstAsync(o => o.OrderId == order.OrderId);
        savedOrder.CouponId.Should().Be(coupon.CouponId);
        savedOrder.ShippingMethodId.Should().Be(shipping.ShippingMethodId);
        savedOrder.DiscountAmount.Should().Be(100m);
        savedOrder.ShippingCost.Should().Be(50m);
        savedOrder.TotalAmount.Should().Be(950m);
    }

    // 9. Cities by Province Endpoint
    [Fact]
    public async Task Cities_GetByProvince_ReturnsOnlyCitiesOfThatProvince()
    {
        var p1 = new Province { ProvinceName = $"P1-{Guid.NewGuid():N}" };
        var p2 = new Province { ProvinceName = $"P2-{Guid.NewGuid():N}" };
        DbContext.Province.AddRange(p1, p2);
        await DbContext.SaveChangesAsync();

        var c1 = new City { CityName = "City1_P1", ProvinceId = p1.ProvinceId };
        var c2 = new City { CityName = "City2_P1", ProvinceId = p1.ProvinceId };
        var c3 = new City { CityName = "City3_P2", ProvinceId = p2.ProvinceId };
        DbContext.City.AddRange(c1, c2, c3);
        await DbContext.SaveChangesAsync();

        var p1Cities = await CityService.GetByProvinceIdAsync(p1.ProvinceId);
        p1Cities.Should().HaveCount(2);
        p1Cities.Select(c => c.CityName).Should().Contain(new[] { "City1_P1", "City2_P1" });
        p1Cities.Select(c => c.CityName).Should().NotContain("City3_P2");
    }
}
