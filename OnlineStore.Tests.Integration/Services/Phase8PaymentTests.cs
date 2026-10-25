using System.Net;
using System.Net.Http.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 8 — پرداخت: idempotency، ماشین حالت، webhook، برگشت وجه،
/// R35 (DeliveredAt)، R36 (sellers/me) و R37 (تسویه در داشبورد).
/// </summary>
public class Phase8PaymentTests : BaseIntegrationTest
{
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private IPaymentSignatureValidator SignatureValidator => GetService<IPaymentSignatureValidator>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase8PaymentTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId, string role = "Customer")
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    // ==================== ماشین حالت ====================

    [Theory]
    [InlineData(PaymentState.Initiated, PaymentState.Succeeded)]
    [InlineData(PaymentState.Initiated, PaymentState.Refunded)]
    [InlineData(PaymentState.Pending, PaymentState.Refunded)]
    [InlineData(PaymentState.Succeeded, PaymentState.Pending)]
    [InlineData(PaymentState.Failed, PaymentState.Succeeded)]
    [InlineData(PaymentState.Refunded, PaymentState.Succeeded)]
    public void PaymentState_InvalidTransitions_AreRejected(PaymentState from, PaymentState to)
    {
        // چیدمان مستقیم وضعیت (TransitionTo فقط برای مسیر مجاز است).
        var payment = new Payment { InvoiceId = 1, Amount = 100, State = from };

        var act = () => payment.TransitionTo(to);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{from}*{to}*");
    }

    [Fact]
    public void PaymentState_HappyPath_IsAllowed()
    {
        var payment = new Payment { InvoiceId = 1, Amount = 100 };

        payment.TransactionId = "AUTH-1";
        payment.TransitionTo(PaymentState.Pending);
        payment.State.Should().Be(PaymentState.Pending);

        payment.GatewayReference = "REF-1";
        payment.TransitionTo(PaymentState.Succeeded);
        payment.State.Should().Be(PaymentState.Succeeded);

        payment.TransitionTo(PaymentState.Refunded);
    }

    // ==================== Idempotency ====================

    [Fact]
    public async Task InitiatePayment_Twice_WithSameKey_ReturnsSamePayment()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(120m);
        SetCurrentUser(buyerUser, "Customer");

        var key = $"key-{Guid.NewGuid():N}";

        var first = await PaymentService.InitiateAsync(order.OrderId, key);
        var second = await PaymentService.InitiateAsync(order.OrderId, key);

        first.IsReplay.Should().BeFalse();
        second.IsReplay.Should().BeTrue();
        second.PaymentId.Should().Be(first.PaymentId);
        second.Authority.Should().Be(first.Authority);

        // و مهم‌تر: فقط یک ردیف در دیتابیس.
        var count = await DbContext.Payment.CountAsync(p => p.OrderId == order.OrderId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task InitiatePayment_SameKeyDifferentOrder_IsRejected()
    {
        var (firstOrder, buyerUser) = await CreatePayableOrderAsync(50m);
        var (secondOrder, _) = await CreatePayableOrderAsync(60m, buyerUser);
        SetCurrentUser(buyerUser, "Customer");

        var key = $"key-{Guid.NewGuid():N}";

        await PaymentService.InitiateAsync(firstOrder.OrderId, key);

        var act = () => PaymentService.InitiateAsync(secondOrder.OrderId, key);

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task InitiatePayment_AnotherUsersOrder_IsForbidden()
    {
        var (order, _) = await CreatePayableOrderAsync(70m);
        var (_, intruderUser) = await CreateCustomerAsync();

        SetCurrentUser(intruderUser.UserId, "Customer");

        var act = () => PaymentService.InitiateAsync(order.OrderId, null);

        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.And.Code.Should().Be("PAYMENT_FORBIDDEN");
    }

    // ==================== تأیید ====================

    [Fact]
    public async Task VerifyPayment_Succeeds_AndMovesOrderToProcessing()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(200m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);

        var verified = await PaymentService.VerifyAsync(initiation.Authority!);

        verified.State.Should().Be(PaymentState.Succeeded);

        var reloaded = await DbContext.Order.AsNoTracking().FirstAsync(o => o.OrderId == order.OrderId);
        reloaded.Status.Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public async Task VerifyPayment_Fails_KeepsOrderForRetry()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(90m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);

        // کنوانسیون FakePaymentGateway: Authority حاوی fail یعنی پرداخت ناموفق.
        var failedAuthority = $"fail-{initiation.Authority}";

        var act = () => PaymentService.VerifyAsync(failedAuthority);
        // این Authority وجود ندارد ⇒ 404؛ پس رکورد را دستی به آن مقدار می‌بریم.
        await act.Should().ThrowAsync<NotFoundException>();

        var payment = await DbContext.Payment.FirstAsync(p => p.PaymentId == initiation.PaymentId);
        payment.TransactionId = failedAuthority;
        await DbContext.SaveChangesAsync();

        var failAct = () => PaymentService.VerifyAsync(failedAuthority);

        var ex = await failAct.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("GATEWAY_FAILED");

        var reloadedPayment = await DbContext.Payment.AsNoTracking()
            .FirstAsync(p => p.PaymentId == initiation.PaymentId);
        reloadedPayment.State.Should().Be(PaymentState.Failed);

        // سفارش همچنان Pending است تا کاربر بتواند دوباره تلاش کند.
        var reloadedOrder = await DbContext.Order.AsNoTracking()
            .FirstAsync(o => o.OrderId == order.OrderId);
        reloadedOrder.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task VerifyPayment_Twice_IsIdempotent()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(30m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);

        await PaymentService.VerifyAsync(initiation.Authority!);
        var again = await PaymentService.VerifyAsync(initiation.Authority!);

        again.State.Should().Be(PaymentState.Succeeded);
        again.PaymentId.Should().Be(initiation.PaymentId);
    }

    // ==================== Webhook ====================

    [Fact]
    public async Task Webhook_WithValidSignature_CompletesPayment()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(75m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);

        var payload = $"{{\"authority\":\"{initiation.Authority}\",\"status\":\"succeeded\"}}";
        var signature = SignatureValidator.Sign(payload);

        var result = await PaymentService.HandleWebhookAsync(payload, signature);

        result.State.Should().Be(PaymentState.Succeeded);
    }

    [Fact]
    public async Task Webhook_WithInvalidSignature_IsRejectedBeforeAnyChange()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(75m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);

        var payload = $"{{\"authority\":\"{initiation.Authority}\",\"status\":\"succeeded\"}}";

        var act = () => PaymentService.HandleWebhookAsync(payload, "deadbeef");

        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.And.Code.Should().Be("INVALID_SIGNATURE");

        // هیچ تغییری نباید رخ داده باشد.
        var payment = await DbContext.Payment.AsNoTracking()
            .FirstAsync(p => p.PaymentId == initiation.PaymentId);
        payment.State.Should().Be(PaymentState.Pending);
    }

    [Fact]
    public async Task Webhook_WithoutSignature_IsRejected()
    {
        var payload = "{\"authority\":\"x\",\"status\":\"succeeded\"}";

        var act = () => PaymentService.HandleWebhookAsync(payload, null);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Webhook_Duplicate_DoesNotRegressState()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(75m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);
        var payload = $"{{\"authority\":\"{initiation.Authority}\",\"status\":\"succeeded\"}}";
        var signature = SignatureValidator.Sign(payload);

        await PaymentService.HandleWebhookAsync(payload, signature);

        // همان webhook دوباره
        var second = await PaymentService.HandleWebhookAsync(payload, signature);

        second.State.Should().Be(PaymentState.Succeeded);
    }

    // ==================== برگشت وجه ====================

    [Fact]
    public async Task OrderCancelled_AfterPayment_PaymentIsRefunded()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(140m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);
        var verified = await PaymentService.VerifyAsync(initiation.Authority!);
        verified.State.Should().Be(PaymentState.Succeeded);

        await OrderService.CancelOrderAsync(order.OrderId);

        var payment = await DbContext.Payment.AsNoTracking()
            .FirstAsync(p => p.PaymentId == initiation.PaymentId);

        payment.State.Should().Be(PaymentState.Refunded);
    }

    [Fact]
    public async Task OrderCancelled_WithoutPayment_DoesNotCreateRefund()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(140m);
        SetCurrentUser(buyerUser, "Customer");

        await OrderService.CancelOrderAsync(order.OrderId);

        var payments = await DbContext.Payment.Where(p => p.OrderId == order.OrderId).ToListAsync();
        payments.Should().BeEmpty();
    }

    // ==================== R36 — sellers/me ====================

    [Fact]
    public async Task SellerMe_ReturnsOwnProfile()
    {
        var (ownerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(ownerUser.UserId);

        var profile = await SellerService.GetByUserIdAsync(ownerUser.UserId);

        profile.Should().NotBeNull();
        profile!.SellerId.Should().Be(seller.SellerId);
        profile.StoreName.Should().Be(seller.StoreName);
    }

    [Fact]
    public async Task SellerMe_ForUserWithoutShop_ReturnsNull()
    {
        var (user, _) = await CreateCustomerAsync();

        var profile = await SellerService.GetByUserIdAsync(user.UserId);

        profile.Should().BeNull();
    }

    [Fact]
    public async Task SellerMe_Unauthenticated_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/api/v1/sellers/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SellerMe_AuthenticatedUserWithoutShop_Returns404()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/sellers/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ==================== R35 — DeliveredAt ====================

    [Fact]
    public async Task MarkDelivered_SetsDeliveredAt()
    {
        var (order, buyerUser, sellerUser) = await CreateDeliveredOrderAsync();

        var group = await DbContext.OrderSellerGroup.AsNoTracking()
            .FirstAsync(g => g.OrderId == order.OrderId);

        group.DeliveredAt.Should().NotBeNull();
        group.DeliveredAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Summary_WithDateRange_FiltersByDeliveredAt()
    {
        var (order, _, sellerUser) = await CreateDeliveredOrderAsync();

        var seller = await DbContext.Seller.AsNoTracking().FirstAsync(s => s.UserId == sellerUser);
        SetCurrentUser(sellerUser, "Seller");

        // بازه‌ای که تحویل در آن است ⇒ یک سفارش
        var inRange = await DashboardService.GetSalesSummaryAsync(
            seller.SellerId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        inRange.TotalOrders.Should().Be(1);

        // بازه‌ای در گذشته دور ⇒ صفر، حتی اگر OrderDate داخل بازه باشد.
        var outOfRange = await DashboardService.GetSalesSummaryAsync(
            seller.SellerId, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-5));

        outOfRange.TotalOrders.Should().Be(0);
    }

    // ==================== R37 — تسویه در داشبورد ====================

    [Fact]
    public async Task Summary_ReportsPendingPayoutsAfterDelivery()
    {
        var (order, _, sellerUser) = await CreateDeliveredOrderAsync();

        var seller = await DbContext.Seller.AsNoTracking().FirstAsync(s => s.UserId == sellerUser);
        SetCurrentUser(sellerUser, "Seller");

        var summary = await DashboardService.GetSalesSummaryAsync(seller.SellerId, null, null);

        // پس از تحویل، SellerPayout با وضعیت Pending ساخته می‌شود.
        summary.PendingPayouts.Should().BeGreaterThan(0m);
        summary.PaidEarnings.Should().Be(0m);

        // و باید با NetEarnings هم‌خوان باشد.
        summary.PendingPayouts.Should().Be(summary.NetEarnings);
    }

    // ==================== Audit ====================

    [Fact]
    public async Task PaymentTransitions_AreWrittenToAuditLog()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(60m);
        SetCurrentUser(buyerUser, "Customer");

        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);
        await PaymentService.VerifyAsync(initiation.Authority!);

        await OrderService.CancelOrderAsync(order.OrderId);

        var actions = await DbContext.AuditLog.AsNoTracking()
            .Where(a => a.EntityName == nameof(Payment))
            .Select(a => a.Action)
            .ToListAsync();

        actions.Should().Contain("Payment.Initiated");
        actions.Should().Contain("Payment.Succeeded");
        actions.Should().Contain("Payment.Refunded");
    }

    // ==================== Helpers ====================

    /// <summary>ورود ادمین seed‌شده (همان مسیری که تست‌های کنترلر استفاده می‌کنند).</summary>
    private async Task<string> GetAdminTokenAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            PhoneNumber = "09123456789",
            Password = "Test@123",
            DeviceId = "test-device"
        });

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return result!.AccessToken;
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; set; } = default!;
    }

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "P8",
            LastName = "User",
            PhoneNumber = $"0912{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (user, customer);
    }

    private async Task<Seller> CreateSellerAsync(int ownerUserId)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"P8Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();
        return seller;
    }

    private async Task<(BusinessLogic.DTOs.Order.OrderDto order, int buyerUserId)> CreatePayableOrderAsync(
        decimal price, int? existingBuyerUserId = null)
    {
        var buyerUserId = existingBuyerUserId;
        var buyerCustomerId = 0;

        if (buyerUserId is null)
        {
            var (buyerUser, buyerCustomer) = await CreateCustomerAsync();
            buyerUserId = buyerUser.UserId;
            buyerCustomerId = buyerCustomer.CustomerId;
        }
        else
        {
            buyerCustomerId = (await DbContext.Customer.AsNoTracking()
                .FirstAsync(c => c.UserId == buyerUserId.Value)).CustomerId;
        }

        var listing = await CreateListingAsync(price);

        SetCurrentUser(buyerUserId.Value);

        var order = await OrderService.CreateEmptyOrderAsync(
            buyerCustomerId, "P8 Buyer", "Tehran", "09120000000");

        await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto
        {
            ListingId = listing.ListingId,
            Quantity = 1
        });

        // مبلغ از دیتابیس خوانده شود تا با آنچه سرویس محاسبه می‌کند یکی باشد.
        var reloaded = await OrderService.GetOrderDetailsAsync(buyerUserId.Value, order.OrderId);
        reloaded.Should().NotBeNull();

        return (order, buyerUserId.Value);
    }

    private async Task<SellerProductDto> CreateListingAsync(decimal price)
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P8-{Guid.NewGuid():N}", Price = price, SubcategoryId = 1
        });

        var variant = (await ProductService.GetVariantsAsync(product.ProductId)).Single();

        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = price,
            Status = SellerProductStatus.Active
        });

        await EnsureStockAsync(seller, listing.ListingId);
        return listing;
    }

    private async Task<(BusinessLogic.DTOs.Order.OrderDto order, int buyerUser, int sellerUser)>
        CreateDeliveredOrderAsync()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(100m);

        await OrderService.ConfirmOrderAsync(order.OrderId);
        await OrderService.MarkDeliveredAsync(order.OrderId);

        var group = await DbContext.OrderSellerGroup.AsNoTracking()
            .FirstAsync(g => g.OrderId == order.OrderId);

        var seller = await DbContext.Seller.AsNoTracking()
            .FirstAsync(s => s.SellerId == group.SellerId);

        return (order, buyerUser, seller.UserId);
    }

    private async Task EnsureStockAsync(Seller seller, int listingId)
    {
        var warehouse = await DbContext.Warehouse.FirstOrDefaultAsync(w => w.SellerId == seller.SellerId);

        if (warehouse is null)
        {
            var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
            DbContext.Province.Add(province);
            await DbContext.SaveChangesAsync();

            var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
            DbContext.City.Add(city);
            await DbContext.SaveChangesAsync();

            var address = new Address
            {
                CityId = city.CityId,
                Plaque = "1",
                Unit = "1",
                PostalCode = "1234567890",
                RecipientFirstName = "P8",
                RecipientLastName = "Shop",
                UserId = seller.UserId
            };
            DbContext.Address.Add(address);
            await DbContext.SaveChangesAsync();

            warehouse = new Warehouse
            {
                Name = $"W-{Guid.NewGuid():N}",
                AddressId = address.AddressId,
                SellerId = seller.SellerId
            };
            DbContext.Warehouse.Add(warehouse);
            await DbContext.SaveChangesAsync();
        }

        DbContext.Inventory.Add(new Inventory(listingId, warehouse.WarehouseId, 100));
        await DbContext.SaveChangesAsync();
    }
}