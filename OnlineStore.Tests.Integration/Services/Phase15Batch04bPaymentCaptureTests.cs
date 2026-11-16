using Application.Entities;
using Application.Interfaces;
using Application.Options;
using AutoMapper;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using DataLayer.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 4b — پس از تأیید موفق درگاه (پول برداشته شده) تأیید پرداخت هرگز نباید شکست بخورد.
///
/// Batch 4 ماشین حالت گروه فروشنده را سخت‌گیر کرد (ApplyCommission فقط از Pending).
/// این تست‌ها ثابت می‌کنند که پس از برداشت پول:
///   • Payment همیشه با مرجع درگاه Succeeded ذخیره می‌شود؛
///   • گذار سفارش/گروه و کمیسیون idempotent است؛
///   • ناسازگاری واقعی با عنوان Payment.ReviewRequired در AuditLog ثبت می‌شود و پاسخ موفق است.
/// </summary>
public class Phase15Batch04bPaymentCaptureTests : ControllerIntegrationTestBase
{
    private const string ReviewAction = "Payment.ReviewRequired";

    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private IPaymentSignatureValidator SignatureValidator => GetService<IPaymentSignatureValidator>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase15Batch04bPaymentCaptureTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== سناریوی اصلی: تأیید ادمین ← پرداخت ====================

    [Fact]
    public async Task Verify_AfterAdminConfirmedUnpaidOrder_Succeeds_AndKeepsLockedCommission()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        await OrderService.ConfirmOrderAsync(seeded.OrderId);
        var commissionsBefore = await CommissionsAsync(seeded.OrderId);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        var result = await PaymentService.VerifyAsync(initiation.Authority!);

        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Processing);
        (await CommissionsAsync(seeded.OrderId)).Should().Equal(commissionsBefore);
        (await ReviewEntriesAsync(initiation.PaymentId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_AfterAdminConfirmedUnpaidOrder_Succeeds()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        await OrderService.ConfirmOrderAsync(seeded.OrderId);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        var result = await SendSucceededWebhookAsync(initiation.Authority!);

        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await ReviewEntriesAsync(initiation.PaymentId)).Should().BeEmpty();
    }

    // ==================== داده‌های باقی‌مانده (پیش از Batch 4) ====================

    [Fact]
    public async Task Verify_PendingOrderWithShippedLeftoverGroup_RecordsSucceeded_AndFlagsForReview()
    {
        // پیش از Batch 4 فروشنده می‌توانست گروه سفارش پرداخت‌نشده را ارسال کند (F-02).
        var seeded = await CreateOrderAsync(sellers: 2);
        await ForceGroupStatusAsync(seeded.GroupIds[0], SellerGroupStatus.Shipped);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        var result = await PaymentService.VerifyAsync(initiation.Authority!);

        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusAsync(seeded.GroupIds[0])).Should().Be(SellerGroupStatus.Shipped);
        (await GroupStatusAsync(seeded.GroupIds[1])).Should().Be(SellerGroupStatus.Processing);

        var review = await ReviewEntriesAsync(initiation.PaymentId);
        review.Should().ContainSingle()
            .Which.Should().Contain($"group {seeded.GroupIds[0]} was Shipped");
    }

    [Fact]
    public async Task Webhook_PendingOrderWithCancelledLeftoverGroup_RecordsSucceeded_AndFlagsForReview()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        await ForceGroupStatusAsync(seeded.GroupIds[1], SellerGroupStatus.Cancelled);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        var result = await SendSucceededWebhookAsync(initiation.Authority!);

        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusAsync(seeded.GroupIds[0])).Should().Be(SellerGroupStatus.Processing);
        (await GroupStatusAsync(seeded.GroupIds[1])).Should().Be(SellerGroupStatus.Cancelled);

        var review = await ReviewEntriesAsync(initiation.PaymentId);
        review.Should().ContainSingle()
            .Which.Should().Contain($"group {seeded.GroupIds[1]} was Cancelled");
    }

    [Fact]
    public async Task Verify_OrderCancelledWhilePaymentPending_RecordsSucceeded_AndFlagsForRefundReview()
    {
        var seeded = await CreateOrderAsync(sellers: 1);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        // Batch 5 (F-05): Verify پس از لغو سفارش دیگر درگاه را صدا نمی‌زند (Phase15Batch05PaymentLifecycleTests).
        // تضمین Batch 4b هنوز برای حالتی لازم است که Verify پیش از لغو آغاز شده بود (پاسخ اول نامعلوم) و تکرار آن پول
        // را برداشته نشان می‌دهد؛ لغو در این فاصله با داده تحمیل می‌شود چون مسیرهای لغو اکنون آن را رد می‌کنند.
        Factory.PaymentGateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));
        var firstVerify = () => PaymentService.VerifyAsync(initiation.Authority!);
        await firstVerify.Should().ThrowAsync<Application.Exceptions.ServiceUnavailableException>();
        await ForceOrderCancelledAsync(seeded.OrderId);

        var result = await PaymentService.VerifyAsync(initiation.Authority!);

        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Cancelled);

        var review = await ReviewEntriesAsync(initiation.PaymentId);
        review.Should().ContainSingle()
            .Which.Should().Contain("order was Cancelled").And.Contain("refundNeeded=true");
    }

    // ==================== خطای زیرساخت پس از برداشت پول ====================

    [Fact]
    public async Task Verify_OrderSaveConflictAfterCapture_StillRecordsSucceeded_AndFlagsForReview()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        // یک ویرایش هم‌زمان روی سفارش (RowVersion) ذخیرهٔ سفارش را رد می‌کند.
        await using var harness = CreateIsolatedPaymentService(
            seeded.BuyerUserId, interceptor: new FailOrderSaveInterceptor());

        var result = await harness.Service.VerifyAsync(initiation.Authority!);

        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);

        var review = await ReviewEntriesAsync(initiation.PaymentId);
        review.Should().ContainSingle()
            .Which.Should().Contain("order sync failed");
    }

    [Fact]
    public async Task Verify_RacingWebhookRecordsPaymentFirst_ReturnsSucceeded()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        var competitor = new CompetingCaptureInterceptor(Factory.Services, initiation.PaymentId);
        await using var harness = CreateIsolatedPaymentService(seeded.BuyerUserId, interceptor: competitor);

        var result = await harness.Service.VerifyAsync(initiation.Authority!);

        competitor.Fired.Should().BeTrue();
        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
    }

    [Fact]
    public async Task Verify_RequestAbortedAfterCapture_StillRecordsSucceeded()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        // مشتری درست پس از پاسخ موفق درگاه اتصال را می‌بندد (RequestAborted).
        using var cts = new CancellationTokenSource();
        await using var harness = CreateIsolatedPaymentService(
            seeded.BuyerUserId, gateway: new CancelAfterCaptureGateway(cts));

        var result = await harness.Service.VerifyAsync(initiation.Authority!, cts.Token);

        cts.IsCancellationRequested.Should().BeTrue();
        result.State.Should().Be(PaymentState.Succeeded);
        await AssertPaymentSucceededAsync(initiation.PaymentId, initiation.Authority!);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
    }

    // ==================== Helpers ====================

    private sealed record SeededOrder(int OrderId, int BuyerUserId, IReadOnlyList<int> GroupIds);

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private Task<PaymentDto> SendSucceededWebhookAsync(string authority)
    {
        var payload = $"{{\"authority\":\"{authority}\",\"status\":\"succeeded\"}}";
        return PaymentService.HandleWebhookAsync(payload, SignatureValidator.Sign(payload));
    }

    private async Task AssertPaymentSucceededAsync(int paymentId, string authority)
    {
        var payment = await DbContext.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == paymentId);
        payment.State.Should().Be(PaymentState.Succeeded);
        payment.GatewayReference.Should().Be($"REF-{authority}");
    }

    private Task<OrderStatus> OrderStatusAsync(int orderId)
        => DbContext.Order.AsNoTracking().Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

    private Task<SellerGroupStatus> GroupStatusAsync(int groupId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderSellerGroupId == groupId).Select(g => g.Status).SingleAsync();

    private Task<List<SellerGroupStatus>> GroupStatusesAsync(int orderId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == orderId).OrderBy(g => g.OrderSellerGroupId).Select(g => g.Status).ToListAsync();

    private Task<List<decimal>> CommissionsAsync(int orderId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == orderId).OrderBy(g => g.OrderSellerGroupId)
            .Select(g => g.CommissionAmount).ToListAsync();

    private Task<List<string>> ReviewEntriesAsync(int paymentId)
        => DbContext.AuditLog.AsNoTracking()
            .Where(a => a.Action == ReviewAction && a.EntityName == nameof(Payment) && a.EntityId == paymentId.ToString())
            .Select(a => a.Details ?? string.Empty)
            .ToListAsync();

    /// <summary>
    /// وضعیت گروه را مستقیم در دیتابیس می‌گذارد؛ شبیه‌سازی داده‌ای که پیش از Batch 4
    /// (setter عمومی، ارسال بدون پرداخت) ساخته شده است.
    /// </summary>
    private async Task ForceGroupStatusAsync(int groupId, SellerGroupStatus status)
    {
        var group = await DbContext.OrderSellerGroup.AsTracking().SingleAsync(g => g.OrderSellerGroupId == groupId);
        DbContext.Entry(group).Property(nameof(OrderSellerGroup.Status)).CurrentValue = status;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task ForceOrderCancelledAsync(int orderId)
    {
        DbContext.ChangeTracker.Clear();
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        DbContext.Entry(order).Property(nameof(Order.Status)).CurrentValue = OrderStatus.Cancelled;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<SeededOrder> CreateOrderAsync(int sellers)
    {
        var listingIds = new List<int>();
        for (var i = 0; i < sellers; i++)
        {
            var (sellerUser, _) = await CreateUserAsync();
            listingIds.Add(await CreateListingAsync(sellerUser, 100m * (i + 1)));
        }

        var (buyerUser, buyer) = await CreateUserAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "B4b Buyer", "Tehran", "09120000000");
        foreach (var listingId in listingIds)
            await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1 });

        var groupIds = await DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == order.OrderId)
            .OrderBy(g => g.OrderSellerGroupId)
            .Select(g => g.OrderSellerGroupId)
            .ToListAsync();

        DbContext.ChangeTracker.Clear();
        return new SeededOrder(order.OrderId, buyerUser.UserId, groupIds);
    }

    private async Task<(User User, Customer Customer)> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "B4b",
            LastName = "User",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
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

    private async Task<int> CreateListingAsync(User sellerUser, decimal price)
    {
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B4bShop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B4b-{Guid.NewGuid():N}",
            Price = price,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece,
            IsActive = true
        };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "Default", IsActive = true };
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
            RecipientFirstName = "B4b",
            RecipientLastName = "Shop",
            UserId = sellerUser.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, 10m));
        await DbContext.SaveChangesAsync();

        return listing.ListingId;
    }

    /// <summary>
    /// PaymentService روی DbContext جدا با interceptor یا درگاه دلخواه؛
    /// همان سیم‌کشی DI، فقط با UnitOfWork و AuditService همان DbContext.
    /// </summary>
    private IsolatedPaymentService CreateIsolatedPaymentService(
        int buyerUserId,
        ISaveChangesInterceptor? interceptor = null,
        IPaymentGateway? gateway = null)
    {
        var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var interceptors = sp.GetServices<ISaveChangesInterceptor>().ToList();
        if (interceptor is not null)
            interceptors.Add(interceptor);

        var context = new TestAppDbContext(sp.GetRequiredService<DbContextOptions<AppDbContext>>(), interceptors);
        var unitOfWork = new UnitOfWork(context);

        var currentUser = (FakeCurrentUserService)sp.GetRequiredService<ICurrentUserService>();
        currentUser.CurrentUserId = buyerUserId;
        currentUser.CurrentUserRole = "Customer";

        var service = new PaymentService(
            unitOfWork,
            sp.GetRequiredService<IMapper>(),
            gateway ?? new FakePaymentGateway(),
            sp.GetRequiredService<IPaymentSignatureValidator>(),
            currentUser,
            new AuditService(unitOfWork, currentUser),
            // Batch 41: دو وابستگی تازهٔ سازنده. این تست‌ها فقط verify/webhook را می‌سنجند و به برگشت وجه نمی‌رسند،
            // پس روال برگرداندن سفارشِ همان scope کافی است؛ ساعت، ساعت تست کارخانه است.
            sp.GetRequiredService<IOrderReversalService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<PaymentGatewaySettings>>(),
            NullLogger<PaymentService>.Instance);

        return new IsolatedPaymentService(service, context, scope);
    }

    private sealed class IsolatedPaymentService : IAsyncDisposable
    {
        private readonly AppDbContext _context;
        private readonly AsyncServiceScope _scope;

        public IsolatedPaymentService(PaymentService service, AppDbContext context, AsyncServiceScope scope)
        {
            Service = service;
            _context = context;
            _scope = scope;
        }

        public PaymentService Service { get; }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            // Batch 41: scope اکنون UnitOfWork دارد (برای IOrderReversalService) که فقط IAsyncDisposable است.
            await _scope.DisposeAsync();
        }
    }

    /// <summary>هر ذخیره‌ای که سفارش را تغییر دهد با تعارض هم‌زمانی (RowVersion) رد می‌شود.</summary>
    private sealed class FailOrderSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Order>().Any(e => e.State == EntityState.Modified))
                throw new DbUpdateConcurrencyException("Simulated concurrent order update.");

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// رقابت قطعی redirect و webhook: درست پیش از ذخیرهٔ Succeeded، درخواست رقیب
    /// (DbContext جدا) همان پرداخت را Succeeded ثبت می‌کند و ذخیرهٔ ما تعارض می‌خورد.
    /// </summary>
    private sealed class CompetingCaptureInterceptor : SaveChangesInterceptor
    {
        private readonly IServiceProvider _root;
        private readonly int _paymentId;

        public CompetingCaptureInterceptor(IServiceProvider root, int paymentId)
        {
            _root = root;
            _paymentId = paymentId;
        }

        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var capturesPayment = eventData.Context!.ChangeTracker.Entries<Payment>()
                .Any(e => e.State == EntityState.Modified
                          && e.Entity.PaymentId == _paymentId
                          && e.Entity.State == PaymentState.Succeeded);

            if (!Fired && capturesPayment)
            {
                Fired = true;

                using var scope = _root.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var payment = await db.Payment.AsTracking().SingleAsync(p => p.PaymentId == _paymentId, cancellationToken);
                payment.GatewayReference = $"REF-{payment.TransactionId}";
                payment.TransitionTo(PaymentState.Succeeded, "competing webhook");
                await db.SaveChangesAsync(cancellationToken);

                throw new DbUpdateConcurrencyException("Simulated: payment already updated by a competing request.");
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>درگاه واقعی-مانند که پس از برداشت موفق، توکن درخواست را لغو می‌کند.</summary>
    private sealed class CancelAfterCaptureGateway : IPaymentGateway
    {
        private readonly FakePaymentGateway _inner = new();
        private readonly CancellationTokenSource _cts;

        public CancelAfterCaptureGateway(CancellationTokenSource cts) => _cts = cts;

        public Task<PaymentInitiationResult> InitiateAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default)
            => _inner.InitiateAsync(request, cancellationToken);

        public async Task<PaymentVerificationResult> VerifyAsync(string authority, decimal amount, CancellationToken cancellationToken = default)
        {
            var result = await _inner.VerifyAsync(authority, amount, cancellationToken);
            _cts.Cancel();
            return result;
        }

        // Batch 43: IPaymentGateway استعلام هم دارد (FIN-05).
        public Task<PaymentInquiryResult> InquireAsync(string authority, CancellationToken cancellationToken = default)
            => _inner.InquireAsync(authority, cancellationToken);

        // Batch 5: IPaymentGateway آدرس صفحهٔ پرداخت را هم می‌سازد (F-28).
        public string BuildRedirectUrl(string authority) => _inner.BuildRedirectUrl(authority);

        public Task<bool> RefundAsync(string authority, decimal amount, CancellationToken cancellationToken = default)
            => _inner.RefundAsync(authority, amount, cancellationToken);
    }
}
