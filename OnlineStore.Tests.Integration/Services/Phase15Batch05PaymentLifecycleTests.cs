using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Services;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 5 — چرخهٔ عمر پرداخت (F-05، F-06، F-07، F-27، F-28).
///
///  • F-05: برای سفارش لغوشده/منقضی Verify درگاه صدا زده نمی‌شود؛ پرداخت با دلیل لغو و در AuditLog ثبت می‌شود.
///  • F-06: سفارش پرداخت‌شده دوباره شروع پرداخت نمی‌گیرد (409) و پرداخت دوم آن Verify نمی‌شود؛ از دو Verify هم‌زمان
///    برای دو پرداخت یک سفارش فقط یکی Succeeded می‌شود.
///  • F-07: timeout/خطای شبکه در Verify پرداخت را Failed نمی‌کند: 503 PAYMENT_VERIFY_RETRY با Retry-After و
///    پرداخت Pending می‌ماند؛ تکرار (Verified یا AlreadyVerified) یک بار و فقط یک بار ثبت می‌شود.
///  • F-27: مبلغ ارسالی به درگاه ریال صحیح و برابر مبلغ ذخیره‌شده است.
///  • F-28: آدرس صفحهٔ پرداخت در replay همان آدرس درگاه است.
/// درگاه: <see cref="ScriptedPaymentGateway"/> (بدون شبکه).
/// </summary>
public class Phase15Batch05PaymentLifecycleTests : ControllerIntegrationTestBase
{
    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private IPaymentSignatureValidator SignatureValidator => GetService<IPaymentSignatureValidator>();
    private AppDbContext DbContext => GetService<AppDbContext>();
    private ScriptedPaymentGateway Gateway => Factory.PaymentGateway;

    public Phase15Batch05PaymentLifecycleTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== F-05: سفارش لغوشده ====================

    [Fact]
    public async Task Verify_AfterCustomerCancelledOrder_DoesNotCallGateway_AndIsAudited()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);

        await OrderService.CancelOrderAsync(seeded.OrderId);

        var act = () => PaymentService.VerifyAsync(initiation.Authority!);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ORDER_NOT_PAYABLE");
        Gateway.VerifyCalls.Should().BeEmpty("an unverified payment is reversed to the payer by the gateway");
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Cancelled);
        (await AuditActionsAsync(initiation.PaymentId)).Should().Contain(new[] { "Payment.Cancelled", "Payment.NotVerified" });
    }

    [Fact]
    public async Task Verify_OrderCancelledWhilePaymentStillPending_DoesNotCallGateway_CancelsPayment_AndIsAudited()
    {
        // سفارشی که پیش از Batch 5 لغو شده (یا با مسیری که پرداخت را باز گذاشته): پرداخت هنوز Pending است.
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        await ForceOrderStatusAsync(seeded.OrderId, OrderStatus.Cancelled);

        var act = () => PaymentService.VerifyAsync(initiation.Authority!);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ORDER_NOT_PAYABLE");
        Gateway.VerifyCalls.Should().BeEmpty();
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Cancelled);
        var notVerified = await AuditDetailsAsync(initiation.PaymentId, "Payment.NotVerified");
        notVerified.Should().ContainSingle().Which.Should().Contain("ORDER_NOT_PAYABLE");
    }

    [Fact]
    public async Task Webhook_AfterOrderCancelled_DoesNotCallGateway()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        await ForceOrderStatusAsync(seeded.OrderId, OrderStatus.Cancelled);

        var act = () => SendSucceededWebhookAsync(initiation.Authority!);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ORDER_NOT_PAYABLE");
        Gateway.VerifyCalls.Should().BeEmpty();
        (await PaymentStateAsync(initiation.PaymentId)).Should().NotBe(PaymentState.Succeeded);
    }

    // ==================== F-06: سفارش پرداخت‌شده ====================

    [Fact]
    public async Task Initiate_OnPaidOrder_Returns409OrderAlreadyPaid()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var first = await PaymentService.InitiateAsync(seeded.OrderId, null);
        await PaymentService.VerifyAsync(first.Authority!);
        var initiationsBefore = Gateway.InitiateCalls.Count;

        var act = () => PaymentService.InitiateAsync(seeded.OrderId, null);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ORDER_ALREADY_PAID");
        Gateway.InitiateCalls.Should().HaveCount(initiationsBefore, "no second gateway authority is created");
        (await DbContext.Payment.AsNoTracking().CountAsync(p => p.OrderId == seeded.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task Verify_SecondPaymentOfPaidOrder_IsNotVerified()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var first = await PaymentService.InitiateAsync(seeded.OrderId, null);
        await PaymentService.VerifyAsync(first.Authority!);
        // پرداخت دومِ هنوز باز برای سفارش پرداخت‌شده (شروع پرداخت روی سفارش Processing پیش از Batch 5 مجاز بود).
        var second = await AddOpenPaymentAsync(seeded.OrderId, first.PaymentId);
        var verifyCallsBefore = Gateway.VerifyCalls.Count;

        var act = () => PaymentService.VerifyAsync(second.Authority);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ORDER_ALREADY_PAID");
        Gateway.VerifyCalls.Should().HaveCount(verifyCallsBefore, "the second payment is left unverified so the gateway reverses it");
        (await AuditDetailsAsync(second.PaymentId, "Payment.NotVerified")).Should().ContainSingle();
        (await PaymentStateAsync(second.PaymentId)).Should().Be(PaymentState.Cancelled);
        (await SucceededCountAsync(seeded.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentVerify_TwoPaymentsOfSameOrder_ExactlyOneSucceeds()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var first = await PaymentService.InitiateAsync(seeded.OrderId, null);
        var second = await AddOpenPaymentAsync(seeded.OrderId, first.PaymentId);

        // در لحظه‌ای که درگاه Verify پرداخت اول را پردازش می‌کند، Verify پرداخت دوم (درخواست دیگر، DbContext دیگر) کامل اجرا می‌شود.
        Exception? competingError = null;
        Gateway.BeforeVerifyReturns = async _ =>
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var competing = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            try
            {
                await competing.VerifyAsync(second.Authority);
            }
            catch (Exception ex)
            {
                competingError = ex;
            }
        };

        var result = await PaymentService.VerifyAsync(first.Authority!);

        result.State.Should().Be(PaymentState.Succeeded);
        DbContext.ChangeTracker.Clear();
        (await SucceededCountAsync(seeded.OrderId)).Should().Be(1);
        Gateway.VerifyCalls.Select(c => c.Authority).Should().Equal(new[] { first.Authority! },
            "the competing payment must not reach the gateway");
        competingError.Should().BeAssignableTo<ConflictException>();
        (await PaymentStateAsync(second.PaymentId)).Should().Be(PaymentState.Cancelled);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
    }

    // ==================== F-07: نتیجهٔ نامعلوم ====================

    [Fact]
    public async Task Verify_Timeout_KeepsPaymentPending_Returns503_AndRetrySucceedsOnce()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));

        var act = () => PaymentService.VerifyAsync(initiation.Authority!);

        var unavailable = (await act.Should().ThrowAsync<ServiceUnavailableException>()).Which;
        unavailable.Code.Should().Be("PAYMENT_VERIFY_RETRY");
        unavailable.RetryAfterSeconds.Should().BePositive();
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Pending);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);

        var retried = await PaymentService.VerifyAsync(initiation.Authority!);
        var again = await PaymentService.VerifyAsync(initiation.Authority!);

        retried.State.Should().Be(PaymentState.Succeeded);
        again.State.Should().Be(PaymentState.Succeeded);
        DbContext.ChangeTracker.Clear();
        (await SucceededCountAsync(seeded.OrderId)).Should().Be(1);
        (await AuditDetailsAsync(initiation.PaymentId, "Payment.Succeeded")).Should().ContainSingle("no double booking");
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Processing);
        (await DbContext.Invoice.AsNoTracking().SingleAsync(i => i.OrderId == seeded.OrderId)).Status.Should().Be(InvoiceStatus.Paid);
        Gateway.VerifyCalls.Should().HaveCount(2, "the third verify is answered from the database");
    }

    [Fact]
    public async Task Verify_AlreadyVerifiedOnRetry_IsTreatedAsSuccess()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_ERROR", "connection reset"));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.AlreadyVerified("REF-already"));

        var first = () => PaymentService.VerifyAsync(initiation.Authority!);
        await first.Should().ThrowAsync<ServiceUnavailableException>();

        var retried = await PaymentService.VerifyAsync(initiation.Authority!);

        retried.State.Should().Be(PaymentState.Succeeded);
        var payment = await DbContext.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == initiation.PaymentId);
        payment.State.Should().Be(PaymentState.Succeeded);
        payment.GatewayReference.Should().Be("REF-already");
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public async Task Verify_DefinitiveRejection_MarksPaymentFailed()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Rejected("GATEWAY_REJECTED", "not paid"));

        var act = () => PaymentService.VerifyAsync(initiation.Authority!);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("GATEWAY_REJECTED"); // Batch 54 (API-06): definitive gateway rejection at verify is 409
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Failed);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task VerifyEndpoint_Timeout_Returns503WithRetryAfter_AndPaymentStaysPending()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_UNAVAILABLE", "HTTP 502"));
        var token = await GetAdminTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/payments/verify?authority={initiation.Authority}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be("PAYMENT_VERIFY_RETRY");
        json.RootElement.GetProperty("retryAfterSeconds").GetInt32().Should().BePositive();
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Pending);
    }

    [Fact]
    public async Task CustomerCancel_WhileVerificationOutcomeUnknown_Returns409_AndKeepsPaymentPending()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));
        var verify = () => PaymentService.VerifyAsync(initiation.Authority!);
        await verify.Should().ThrowAsync<ServiceUnavailableException>();

        var cancel = () => OrderService.CancelOrderAsync(seeded.OrderId);

        (await cancel.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PAYMENT_VERIFICATION_PENDING");
        DbContext.ChangeTracker.Clear();
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Pending);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task AbandonedCleanup_WhileVerificationOutcomeUnknown_DoesNotCancel_AndFlagsForReview()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));
        var verify = () => PaymentService.VerifyAsync(initiation.Authority!);
        await verify.Should().ThrowAsync<ServiceUnavailableException>();
        await AgeOrderAsync(seeded.OrderId);

        var cancelled = await NewCleanup().RunCleanupAsync();
        await NewCleanup().RunCleanupAsync();

        cancelled.Should().Be(0);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Pending);
        (await AuditDetailsAsync(initiation.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle()
            .Which.Should().Contain("verification outcome is unknown");

        // تأیید دوباره وضعیت را قطعی می‌کند.
        var resolved = await PaymentService.VerifyAsync(initiation.Authority!);
        resolved.State.Should().Be(PaymentState.Succeeded);
    }

    [Fact]
    public async Task AbandonedPaymentSweep_WhileVerificationOutcomeUnknown_DoesNotCancel_AndFlagsForReview()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));
        var verify = () => PaymentService.VerifyAsync(initiation.Authority!);
        await verify.Should().ThrowAsync<ServiceUnavailableException>();
        await AgePaymentAsync(initiation.PaymentId, TimeSpan.FromHours(2));

        await GetService<IAbandonedPaymentSweeper>().SweepAsync();
        await GetService<IAbandonedPaymentSweeper>().SweepAsync();

        DbContext.ChangeTracker.Clear();
        (await PaymentStateAsync(initiation.PaymentId)).Should().Be(PaymentState.Pending);
        (await AuditDetailsAsync(initiation.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle()
            .Which.Should().Contain("verification outcome is unknown");
    }

    // ==================== F-27: مبلغ ریالی ====================

    [Fact]
    public async Task AmountSentToGateway_EqualsOrderTotalInRial_AtInitiateAndVerify()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var total = await OrderTotalAsync(seeded.OrderId);

        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        await PaymentService.VerifyAsync(initiation.Authority!);

        Gateway.InitiateCalls.Should().ContainSingle().Which.Amount.Should().Be(total);
        Gateway.VerifyCalls.Should().ContainSingle().Which.Amount.Should().Be(total);
        initiation.Amount.Should().Be(total);
        (await DbContext.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == initiation.PaymentId)).Amount.Should().Be(total);
    }

    [Fact]
    public async Task FractionalRialTotal_IsRefused_AndNothingIsSentToTheGateway()
    {
        // Batch 45 (FIN-06، N6): پیش‌تر (نام قبلی: FractionalRialTotal_StoredPaymentAmountEqualsTheWholeRialAmountSentToGateway)
        // کسر ریال بی‌صدا حذف و ۱٬۸۵۱ ریال برداشت می‌شد. اکنون PricingService همهٔ مبالغ را ریال کامل می‌سازد و
        // جمع کسری فقط از دادهٔ خراب می‌آید؛ کف گرفتن به یک assertion تبدیل شده و پرداخت شروع نمی‌شود.
        var seeded = await CreateOrderAsync(sellers: 1);
        await ForceOrderTotalAsync(seeded.OrderId, 1851.75m);

        var act = () => PaymentService.InitiateAsync(seeded.OrderId, null);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).Which.Code.Should().Be("AMOUNT_NOT_WHOLE_RIAL");
        Gateway.InitiateCalls.Should().BeEmpty();
        (await DbContext.Payment.AsNoTracking().AnyAsync(p => p.OrderId == seeded.OrderId)).Should().BeFalse();
    }

    // ==================== F-06 / F-28: پرداخت باز و replay ====================

    [Fact]
    public async Task Initiate_TwiceOnPendingOrder_ReturnsTheSameOpenPayment()
    {
        var seeded = await CreateOrderAsync(sellers: 1);

        var first = await PaymentService.InitiateAsync(seeded.OrderId, null);
        var second = await PaymentService.InitiateAsync(seeded.OrderId, null);

        second.PaymentId.Should().Be(first.PaymentId);
        second.Authority.Should().Be(first.Authority);
        second.IsReplay.Should().BeTrue();
        second.RedirectUrl.Should().Be(first.RedirectUrl);
        Gateway.InitiateCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Initiate_AfterReuseWindow_CancelsTheOldOpenPayment_WhichCanNoLongerBeVerified()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var old = await PaymentService.InitiateAsync(seeded.OrderId, null);
        await AgePaymentAsync(old.PaymentId, TimeSpan.FromHours(1));

        var fresh = await PaymentService.InitiateAsync(seeded.OrderId, null);

        fresh.PaymentId.Should().NotBe(old.PaymentId);
        fresh.IsReplay.Should().BeFalse();
        (await PaymentStateAsync(old.PaymentId)).Should().Be(PaymentState.Cancelled);

        var verifyOld = () => PaymentService.VerifyAsync(old.Authority!);
        await verifyOld.Should().ThrowAsync<ConflictException>();
        Gateway.VerifyCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task IdempotentReplay_RedirectUrl_IsTheGatewayPaymentPage()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var key = $"b5-{Guid.NewGuid():N}";

        var first = await PaymentService.InitiateAsync(seeded.OrderId, key);
        var replay = await PaymentService.InitiateAsync(seeded.OrderId, key);

        replay.IsReplay.Should().BeTrue();
        replay.RedirectUrl.Should().Be(first.RedirectUrl);
    }

    // ==================== helpers ====================

    private sealed record SeededOrder(int OrderId, int BuyerUserId);

    private sealed record OpenPayment(int PaymentId, string Authority);

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

    private AbandonedOrderCleanupService NewCleanup()
        => new(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AbandonedOrderCleanupService>.Instance);

    private Task<PaymentState> PaymentStateAsync(int paymentId)
        => DbContext.Payment.AsNoTracking().Where(p => p.PaymentId == paymentId).Select(p => p.State).SingleAsync();

    private Task<int> SucceededCountAsync(int orderId)
        => DbContext.Payment.AsNoTracking().CountAsync(p => p.OrderId == orderId && p.State == PaymentState.Succeeded);

    private Task<OrderStatus> OrderStatusAsync(int orderId)
        => DbContext.Order.AsNoTracking().Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

    private Task<decimal> OrderTotalAsync(int orderId)
        => DbContext.Order.AsNoTracking().Where(o => o.OrderId == orderId).Select(o => o.TotalAmount).SingleAsync();

    private Task<List<SellerGroupStatus>> GroupStatusesAsync(int orderId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == orderId).Select(g => g.Status).ToListAsync();

    private Task<List<string>> AuditActionsAsync(int paymentId)
        => DbContext.AuditLog.AsNoTracking()
            .Where(a => a.EntityName == nameof(Payment) && a.EntityId == paymentId.ToString())
            .Select(a => a.Action)
            .ToListAsync();

    private Task<List<string>> AuditDetailsAsync(int paymentId, string action)
        => DbContext.AuditLog.AsNoTracking()
            .Where(a => a.Action == action && a.EntityName == nameof(Payment) && a.EntityId == paymentId.ToString())
            .Select(a => a.Details ?? string.Empty)
            .ToListAsync();

    /// <summary>پرداخت باز دوم برای همان سفارش و فاکتور (شبیه دو تب یا دادهٔ پیش از Batch 5).</summary>
    private async Task<OpenPayment> AddOpenPaymentAsync(int orderId, int siblingPaymentId)
    {
        var sibling = await DbContext.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == siblingPaymentId);
        var payment = new Payment
        {
            Amount = sibling.Amount,
            OrderId = orderId,
            InvoiceId = sibling.InvoiceId,
            Gateway = sibling.Gateway,
            TransactionId = $"FAKE-second-{Guid.NewGuid():N}",
            State = PaymentState.Initiated
        };
        payment.TransitionTo(PaymentState.Pending);
        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return new OpenPayment(payment.PaymentId, payment.TransactionId);
    }

    private async Task ForceOrderStatusAsync(int orderId, OrderStatus status)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        DbContext.Entry(order).Property(nameof(Order.Status)).CurrentValue = status;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task ForceOrderTotalAsync(int orderId, decimal total)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        DbContext.Entry(order).Property(nameof(Order.TotalAmount)).CurrentValue = total;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task AgeOrderAsync(int orderId)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        order.OrderDate = DateTime.UtcNow.AddMinutes(-75);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task AgePaymentAsync(int paymentId, TimeSpan age)
    {
        var payment = await DbContext.Payment.AsTracking().SingleAsync(p => p.PaymentId == paymentId);
        payment.LastStateChangedAtUtc = DateTime.UtcNow.Subtract(age);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<SeededOrder> CreateOrderAsync(int sellers)
    {
        var listingIds = new List<int>();
        for (var i = 0; i < sellers; i++)
        {
            var (sellerUser, _) = await CreateUserAsync();
            listingIds.Add(await CreateListingAsync(sellerUser, 100_000m * (i + 1)));
        }

        var (buyerUser, buyer) = await CreateUserAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "B5 Buyer", "Tehran", "09120000000");
        foreach (var listingId in listingIds)
            await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1 });

        DbContext.ChangeTracker.Clear();
        return new SeededOrder(order.OrderId, buyerUser.UserId);
    }

    private async Task<(User User, Customer Customer)> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "B5",
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
            StoreName = $"B5Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B5-{Guid.NewGuid():N}",
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
            RecipientFirstName = "B5",
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
}
