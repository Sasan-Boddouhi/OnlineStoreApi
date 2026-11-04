using System.Net;
using System.Text;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Coupon;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Payout;
using BusinessLogic.DTOs.Shipping;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Services;

public class M712CriticalFixesTests : BaseIntegrationTest
{
    private ICartService CartService => GetService<ICartService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private ICouponService CouponService => GetService<ICouponService>();
    private IShippingMethodService ShippingMethodService => GetService<IShippingMethodService>();
    private IUserService UserService => GetService<IUserService>();
    private IUnitOfWork UnitOfWork => GetService<IUnitOfWork>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public M712CriticalFixesTests(IntegrationTestFactory<Program> factory) : base(factory) { }

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

    // 1. Coupon Financial Distribution to SellerGroups
    [Fact]
    public async Task Coupon_DiscountIsDistributedProportionally_ToSellerGroups_AndDeductedFromSellerPayable()
    {
        var (sellerA, _, _, listingA) = await SetupListingWithStockAsync(price: 600m, stock: 10m);
        var (sellerB, _, _, listingB) = await SetupListingWithStockAsync(price: 400m, stock: 10m);

        // 10% coupon: total 1000 => 100 discount (60 to A, 40 to B)
        var coupon = await CouponService.CreateAsync(new CreateCouponDto
        {
            Code = $"OFF-{Guid.NewGuid():N}",
            DiscountPercentage = 10m,
            IsActive = true
        });

        var buyerUser = new User
        {
            FirstName = "Buyer",
            LastName = "Test",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(buyerUser);
        await DbContext.SaveChangesAsync();
        DbContext.Customer.Add(new Customer { UserId = buyerUser.UserId });
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyerUser.UserId, "Customer");
        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = listingA.ListingId, Quantity = 1 });
        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = listingB.ListingId, Quantity = 1 });

        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer Multi",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09121112233",
            CouponCode = coupon.Code
        });

        order.TotalAmount.Should().Be(900m);
        order.DiscountAmount.Should().Be(100m);

        var groups = await DbContext.OrderSellerGroup
            .Where(g => g.OrderId == order.OrderId)
            .ToListAsync();

        groups.Should().HaveCount(2);
        var groupA = groups.First(g => g.SellerId == sellerA.SellerId);
        var groupB = groups.First(g => g.SellerId == sellerB.SellerId);

        groupA.DiscountAmount.Should().Be(60m);
        groupB.DiscountAmount.Should().Be(40m);

        // SellerPayable before commission snapshot reflects discount deduction
        groupA.SellerPayable.Should().BeLessThan(groupA.SubTotal);
    }

    // 2. Coupon Usage Limits (Global & Per-User)
    [Fact]
    public async Task Coupon_UsageLimits_PreventExcessiveUse()
    {
        var (_, _, _, listing) = await SetupListingWithStockAsync(price: 100m, stock: 10m);

        var coupon = await CouponService.CreateAsync(new CreateCouponDto
        {
            Code = $"LIMIT-{Guid.NewGuid():N}",
            DiscountPercentage = 10m,
            UsageLimit = 1, // Only 1 total use!
            UsageLimitPerUser = 1,
            IsActive = true
        });

        var buyerA = new User
        {
            FirstName = "BuyerA",
            LastName = "Test",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(buyerA);
        await DbContext.SaveChangesAsync();
        DbContext.Customer.Add(new Customer { UserId = buyerA.UserId });
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyerA.UserId, "Customer");
        await CartService.AddItemAsync(buyerA.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        // First checkout succeeds
        var order1 = await CartService.CheckoutAsync(buyerA.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer A",
            ShippingAddress = "Address A",
            ShippingPhoneNumber = "09120000001",
            CouponCode = coupon.Code
        });
        order1.Should().NotBeNull();

        // Second checkout with same coupon fails because UsageLimit hit!
        var buyerB = new User
        {
            FirstName = "BuyerB",
            LastName = "Test",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(buyerB);
        await DbContext.SaveChangesAsync();
        DbContext.Customer.Add(new Customer { UserId = buyerB.UserId });
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyerB.UserId, "Customer");
        await CartService.AddItemAsync(buyerB.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var act = () => CartService.CheckoutAsync(buyerB.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer B",
            ShippingAddress = "Address B",
            ShippingPhoneNumber = "09120000002",
            CouponCode = coupon.Code
        });

        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*ظرفیت استفاده*");
    }

    // 3. Order State Machine: MarkShipped requires Processing, Cancel blocks Returned
    [Fact]
    public async Task Order_InvalidStateTransitions_ThrowInvalidOperationException()
    {
        var order = new Order
        {
            ShippingFullName = "Test",
            ShippingAddress = "Test Address",
            ShippingPhoneNumber = "09120000000"
        };

        // 1. Pending order cannot be marked shipped directly
        var actShip = () => order.MarkShipped();
        actShip.Should().Throw<InvalidOperationException>()
            .WithMessage("*processing*");

        // Confirm to processing
        order.Confirm();
        order.MarkShipped();
        order.MarkDelivered();

        // 2. Delivered order marks returned
        order.MarkReturned();
        order.Status.Should().Be(OrderStatus.Returned);

        // 3. Returned order cannot be cancelled
        var actCancel = () => order.Cancel();
        actCancel.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot cancel*");
    }

    // 4. Seller Clawback on Return
    [Fact]
    public async Task Order_MarkReturned_CreatesSellerDebt_IfPayoutAlreadyPaid()
    {
        var (seller, _, _, listing) = await SetupListingWithStockAsync(100m, 10m);

        var buyer = new User
        {
            FirstName = "Buyer",
            LastName = "Clawback",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(buyer);
        await DbContext.SaveChangesAsync();
        DbContext.Customer.Add(new Customer { UserId = buyer.UserId });
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyer.UserId, "Customer");
        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var order = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer Clawback",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000"
        });

        SetCurrentUser(buyer.UserId, "Customer");
        await OrderService.ConfirmOrderAsync(order.OrderId);

        // Deliver order
        SetCurrentUser(1, "Admin");
        await OrderService.MarkDeliveredAsync(order.OrderId);

        // Mark payout as Paid
        var payout = await DbContext.SellerPayout.FirstAsync(p => p.SellerId == seller.SellerId);
        payout.MarkPaid("BANK-REF-123");
        await DbContext.SaveChangesAsync();

        // Now Admin marks order as Returned
        await OrderService.MarkReturnedAsync(order.OrderId);

        // SellerDebt record MUST be created for the paid payout amount!
        var debt = await DbContext.SellerDebt
            .FirstOrDefaultAsync(d => d.SellerId == seller.SellerId);

        debt.Should().NotBeNull();
        debt!.Amount.Should().Be(payout.Amount);
        debt.IsSettled.Should().BeFalse();
        debt.Reason.Should().Contain("Clawback");
    }

    // 5. File Upload Magic Byte Validation
    [Fact]
    public async Task LocalStorageService_RejectsFile_WithMismatchedMagicBytes()
    {
        var settings = Options.Create(new StorageSettings
        {
            RootPath = "wwwroot/uploads",
            PublicBaseUrl = "/uploads",
            MaxFileSizeBytes = 5 * 1024 * 1024,
            AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" }
        });

        var storageService = new LocalStorageService(settings, NullLogger<LocalStorageService>.Instance);

        // A fake text/php shell with .jpg extension
        var fakePhpContent = "<?php echo 'shell'; ?>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fakePhpContent));

        var act = () => storageService.SaveAsync(stream, "shell.jpg", "image/jpeg");
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*هم‌خوانی ندارد*");
    }

    // 6. User Deletion with Sessions & RefreshTokens
    [Fact]
    public async Task UserService_DeleteAsync_DeletesUserWithTokens_WithoutFKCrash()
    {
        var user = new User
        {
            FirstName = "Delete",
            LastName = "Me",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            DeviceId = "dev-del",
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow,
            AbsoluteExpiryUtc = DateTime.UtcNow.AddDays(7),
            Status = UserSession.SessionStatus.Active
        };
        DbContext.UserSession.Add(session);
        await DbContext.SaveChangesAsync();

        var token = new RefreshTokenEntity
        {
            UserId = user.UserId,
            SessionId = session.Id,
            TokenHash = "hash",
            TokenIdentifier = "ident",
            CreatedAt = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            AbsoluteExpiry = DateTime.UtcNow.AddDays(7),
            FamilyCreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };
        DbContext.RefreshToken.Add(token);
        await DbContext.SaveChangesAsync();

        // Delete user via UserService
        var deleted = await UserService.DeleteAsync(user.UserId);
        deleted.Should().BeTrue();

        (await DbContext.User.AnyAsync(u => u.UserId == user.UserId)).Should().BeFalse();
        (await DbContext.UserSession.AnyAsync(s => s.UserId == user.UserId)).Should().BeFalse();
        (await DbContext.RefreshToken.AnyAsync(r => r.UserId == user.UserId)).Should().BeFalse();
    }

    // 7. UnitOfWork Rollback clears ChangeTracker
    [Fact]
    public async Task UnitOfWork_RollbackTransaction_ClearsChangeTracker()
    {
        await UnitOfWork.BeginTransactionAsync();

        var user = new User
        {
            FirstName = "Tracked",
            LastName = "Rollback",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        await UnitOfWork.Repository<User>().AddAsync(user);

        // Before rollback, entry is Added
        DbContext.ChangeTracker.Entries<User>().Should().Contain(e => e.Entity.PhoneNumber == user.PhoneNumber);

        // Rollback transaction
        await UnitOfWork.RollbackTransactionAsync();

        // ChangeTracker must be cleared!
        DbContext.ChangeTracker.Entries<User>().Should().BeEmpty();
    }

    // 8. Idempotent Checkout Replay with IdempotencyKey
    [Fact]
    public async Task Checkout_IdempotencyKey_ReturnsSameOrder_OnDuplicateSubmit()
    {
        var (seller, _, _, listing) = await SetupListingWithStockAsync(price: 150m, stock: 10m);

        var buyer = new User
        {
            FirstName = "Idempotent",
            LastName = "Buyer",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(buyer);
        await DbContext.SaveChangesAsync();
        DbContext.Customer.Add(new Customer { UserId = buyer.UserId });
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyer.UserId, "Customer");
        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var key = $"IDEMP-{Guid.NewGuid():N}";
        var order1 = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Idempotent Buyer",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09120000001",
            IdempotencyKey = key
        });

        // Submit duplicate checkout with the same IdempotencyKey
        var order2 = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Idempotent Buyer",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09120000001",
            IdempotencyKey = key
        });

        order1.OrderId.Should().Be(order2.OrderId);
        order1.TotalAmount.Should().Be(order2.TotalAmount);
    }

    // 9. Coupon Quota Restoration on Cancellation
    [Fact]
    public async Task Checkout_RestoresCouponQuota_WhenOrderCancelled()
    {
        var (_, _, _, listing) = await SetupListingWithStockAsync(price: 100m, stock: 10m);

        var coupon = await CouponService.CreateAsync(new CreateCouponDto
        {
            Code = $"RESTORE-{Guid.NewGuid():N}",
            DiscountPercentage = 15m,
            UsageLimit = 1,
            UsageLimitPerUser = 1,
            IsActive = true
        });

        var buyer = new User
        {
            FirstName = "Restore",
            LastName = "Buyer",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer
        };
        DbContext.User.Add(buyer);
        await DbContext.SaveChangesAsync();
        DbContext.Customer.Add(new Customer { UserId = buyer.UserId });
        await DbContext.SaveChangesAsync();

        SetCurrentUser(buyer.UserId, "Customer");
        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var order = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Restore Buyer",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09120000002",
            CouponCode = coupon.Code
        });

        // UsedCount should now be 1
        var reloadedCoupon = await DbContext.Coupon.FirstAsync(c => c.CouponId == coupon.CouponId);
        reloadedCoupon.UsedCount.Should().Be(1);

        // Cancel order
        await OrderService.CancelOrderAsync(order.OrderId);

        // UsedCount should be restored to 0!
        reloadedCoupon = await DbContext.Coupon.FirstAsync(c => c.CouponId == coupon.CouponId);
        reloadedCoupon.UsedCount.Should().Be(0);

        // Customer can reuse the coupon!
        await CartService.AddItemAsync(buyer.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });
        var order2 = await CartService.CheckoutAsync(buyer.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Restore Buyer",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09120000002",
            CouponCode = coupon.Code
        });

        order2.Should().NotBeNull();
        order2.DiscountAmount.Should().Be(15m);
    }

    // 10. WebP 8..11 Magic Byte Signature Verification
    [Fact]
    public async Task LocalStorageService_RejectsWav_WithWebpExtension()
    {
        var settings = Options.Create(new StorageSettings
        {
            RootPath = "wwwroot/uploads",
            PublicBaseUrl = "/uploads",
            MaxFileSizeBytes = 5 * 1024 * 1024,
            AllowedExtensions = new[] { ".webp" }
        });
        var storage = new LocalStorageService(settings, NullLogger<LocalStorageService>.Instance);

        // RIFF....WAVE header
        var wavHeader = new byte[12];
        wavHeader[0] = 0x52; wavHeader[1] = 0x49; wavHeader[2] = 0x46; wavHeader[3] = 0x46; // RIFF
        wavHeader[8] = 0x57; wavHeader[9] = 0x41; wavHeader[10] = 0x56; wavHeader[11] = 0x45; // WAVE

        using var stream = new MemoryStream(wavHeader);
        var act = () => storage.SaveAsync(stream, "audio.webp", "audio/wav");

        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*هم‌خوانی ندارد*");
    }

    // 11. SellerDebt Settlement on Next Payout
    [Fact]
    public async Task SellerPayout_SettlesOutstandingSellerDebt()
    {
        var (seller, _, _, _) = await SetupListingWithStockAsync(price: 1000m);

        var debt = new SellerDebt
        {
            SellerId = seller.SellerId,
            Amount = 300m,
            Reason = "Clawback test",
            IsSettled = false
        };
        DbContext.SellerDebt.Add(debt);
        await DbContext.SaveChangesAsync();

        var payout = new SellerPayout(seller.SellerId, null, 800m, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow);
        DbContext.SellerPayout.Add(payout);
        await DbContext.SaveChangesAsync();

        var payoutService = GetService<ISellerPayoutService>();
        await payoutService.MarkAsPaidAsync(payout.SellerPayoutId, "BANK-SETTLE-001");

        var reloadedDebt = await DbContext.SellerDebt.FirstAsync(d => d.SellerDebtId == debt.SellerDebtId);
        reloadedDebt.IsSettled.Should().BeTrue();
    }
}
