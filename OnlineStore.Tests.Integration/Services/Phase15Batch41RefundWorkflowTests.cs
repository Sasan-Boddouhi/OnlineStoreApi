using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Dashboard;
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
/// Batch 41 — گردش‌کار برگشت وجه و برگرداندن سفارش (FIN-01، FIN-03، FIN-11).
///
///  • FIN-01: لغو سفارش پرداخت‌شده پرداخت را <see cref="PaymentState.RefundPending"/> می‌کند، نه Refunded؛ مرجوعی ادمین هم
///    درخواست برگشت وجه ثبت می‌کند؛ برگشت وجه ادمین گروه‌ها را لغو و موجودی را آزاد می‌کند. همهٔ مسیرها (لغو مشتری،
///    لغو ادمین، برگشت وجه ادمین، مرجوعی ادمین، جاب سفارش رهاشده) از یک روال (<c>IOrderReversalService</c>) می‌گذرند.
///  • FIN-03: مشتری پس از ارسال یا تحویل حتی یک بسته نمی‌تواند سفارش را لغو کند: 409 ORDER_PARTLY_SHIPPED و هیچ تغییری.
///  • FIN-11: «تأیید آغازشده» از ستون <see cref="Payment.VerifyStartedAtUtc"/> خوانده می‌شود، نه از AuditLog.
/// درگاه: <see cref="ScriptedPaymentGateway"/> (بدون شبکه). ساعت: <see cref="TestTimeProvider"/> کارخانهٔ تست.
/// </summary>
public class Phase15Batch41RefundWorkflowTests : ControllerIntegrationTestBase
{
    private const decimal InitialStock = 10m;

    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private IInventoryService InventoryService => GetService<IInventoryService>();
    private IPaymentSignatureValidator SignatureValidator => GetService<IPaymentSignatureValidator>();
    private AppDbContext DbContext => GetService<AppDbContext>();
    private ScriptedPaymentGateway Gateway => Factory.PaymentGateway;
    private DateTime ClockNow => Factory.Clock.GetUtcNow().UtcDateTime;

    public Phase15Batch41RefundWorkflowTests(IntegrationTestFactory<Program> factory) : base(factory)
    {
        // ساعت تست میان تست‌های این کلاس مشترک است؛ هر تست از «اکنون» واقعی شروع می‌کند.
        Factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    // ==================== ماشین حالت پرداخت ====================

    [Theory]
    [InlineData(PaymentState.Succeeded, PaymentState.RefundPending, true)]
    [InlineData(PaymentState.RefundPending, PaymentState.Refunded, true)]
    [InlineData(PaymentState.Succeeded, PaymentState.Refunded, false)]
    [InlineData(PaymentState.RefundPending, PaymentState.Succeeded, false)]
    [InlineData(PaymentState.RefundPending, PaymentState.Cancelled, false)]
    [InlineData(PaymentState.Pending, PaymentState.RefundPending, false)]
    [InlineData(PaymentState.Refunded, PaymentState.RefundPending, false)]
    public void PaymentStateMachine_RefundAlwaysGoesThroughRefundPending(PaymentState from, PaymentState to, bool allowed)
    {
        Payment.IsAllowed(from, to).Should().Be(allowed);
    }

    // ==================== FIN-01: لغو سفارش پرداخت‌شده ====================

    [Fact]
    public async Task CustomerCancel_PaidOrder_MovesPaymentToRefundPending_CancelsGroups_AndReleasesStock()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var paid = await PayAsync(seeded);
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var result = await OrderService.CancelOrderAsync(seeded.OrderId);

        result.Status.Should().Be(nameof(OrderStatus.Cancelled));
        DbContext.ChangeTracker.Clear();

        var payment = await PaymentAsync(paid.PaymentId);
        payment.State.Should().Be(PaymentState.RefundPending, "no money has moved yet; an operator must return it");
        payment.RefundRequestedAtUtc.Should().Be(ClockNow);
        payment.RefundedAtUtc.Should().BeNull();
        payment.RefundReason.Should().NotBeNullOrWhiteSpace();

        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Cancelled);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Cancelled);
        foreach (var listingId in seeded.ListingIds)
            (await AvailableAsync(listingId)).Should().Be(InitialStock);

        (await AuditDetailsAsync(nameof(Payment), paid.PaymentId, "Payment.RefundRequested")).Should().ContainSingle();
        (await AuditDetailsAsync(nameof(Payment), paid.PaymentId, "Payment.Refunded")).Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerCancel_PaidOrderWithCoupon_RestoresCouponQuotaOnce()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var couponId = await AttachUsedCouponAsync(seeded.OrderId);
        var paid = await PayAsync(seeded);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        await OrderService.CancelOrderAsync(seeded.OrderId);
        await OrderService.CancelOrderAsync(seeded.OrderId);

        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.RefundPending);
        (await DbContext.Coupon.AsNoTracking().SingleAsync(c => c.CouponId == couponId)).UsedCount.Should().Be(0);
        (await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == seeded.OrderId)).CouponQuotaRefunded.Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_Twice_DoesNotReleaseStockTwice()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        (await AvailableAsync(seeded.ListingIds[0])).Should().Be(InitialStock - 1);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        await OrderService.CancelOrderAsync(seeded.OrderId);
        await OrderService.CancelOrderAsync(seeded.OrderId);

        DbContext.ChangeTracker.Clear();
        (await AvailableAsync(seeded.ListingIds[0])).Should().Be(InitialStock, "stock is released once, when the parcel is cancelled");
    }

    // ==================== FIN-03: لغو مشتری پس از ارسال ====================

    [Fact]
    public async Task CustomerCancel_WithOneShippedParcel_Returns409OrderPartlyShipped_AndChangesNothing()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var paid = await PayAsync(seeded);
        await ShipAsync(seeded, 0);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var act = () => OrderService.CancelOrderAsync(seeded.OrderId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_PARTLY_SHIPPED");
        ex.Which.StatusCode.Should().Be(409);

        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusAsync(seeded.Groups[0].GroupId)).Should().Be(SellerGroupStatus.Shipped);
        (await GroupStatusAsync(seeded.Groups[1].GroupId)).Should().Be(SellerGroupStatus.Processing);
        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.Succeeded);
        foreach (var listingId in seeded.ListingIds)
            (await AvailableAsync(listingId)).Should().Be(InitialStock - 1, "nothing is released");
        (await AuditDetailsAsync(nameof(Order), seeded.OrderId, "Order.Cancelled")).Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerCancel_WithOneDeliveredParcel_Returns409OrderPartlyShipped_AndKeepsThePayout()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var paid = await PayAsync(seeded);
        await ShipAsync(seeded, 0);
        await DeliverAsync(seeded, 0);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var act = () => OrderService.CancelOrderAsync(seeded.OrderId);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ORDER_PARTLY_SHIPPED");

        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusAsync(seeded.Groups[0].GroupId)).Should().Be(SellerGroupStatus.Delivered);
        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.Succeeded);
        (await PayoutsAsync(seeded.OrderId)).Should().ContainSingle().Which.Status.Should().Be(PayoutStatus.Pending);
    }

    [Fact]
    public async Task AdminCancel_PartlyShippedOrder_RequestsRefund_AndReleasesOnlyTheStockThatNeverLeft()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var paid = await PayAsync(seeded);
        await ShipAsync(seeded, 0);

        SetCurrentUser(1, "Admin");
        await OrderService.CancelOrderAsync(seeded.OrderId);

        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Cancelled);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Cancelled);
        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.RefundPending);
        (await AvailableAsync(seeded.ListingIds[0])).Should().Be(InitialStock - 1, "the shipped parcel is in transit, not back in the warehouse");
        (await AvailableAsync(seeded.ListingIds[1])).Should().Be(InitialStock, "the parcel that never left is back on sale");
    }

    // ==================== FIN-01: برگشت وجه ادمین ====================

    [Fact]
    public async Task AdminRefund_ProcessingOrder_CancelsGroups_ReleasesStock_AndRecordsTheRefund()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var couponId = await AttachUsedCouponAsync(seeded.OrderId);
        var paid = await PayAsync(seeded);
        Factory.Clock.Advance(TimeSpan.FromMinutes(7));

        SetCurrentUser(1, "Admin");
        var dto = await PaymentService.RefundPaymentAsync(paid.PaymentId, "duplicate charge");

        // Batch 42 (FIN-12): برگشت وجه ادمین فقط درخواست است؛ Refunded پس از تأیید با شمارهٔ پیگیری بانکی.
        dto.State.Should().Be(PaymentState.RefundPending, "no money has moved until the admin confirms the bank transfer");
        DbContext.ChangeTracker.Clear();

        var payment = await PaymentAsync(paid.PaymentId);
        payment.State.Should().Be(PaymentState.RefundPending);
        payment.RefundRequestedAtUtc.Should().Be(ClockNow);
        payment.RefundedAtUtc.Should().BeNull();
        payment.RefundReason.Should().Be("duplicate charge");

        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Cancelled);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Cancelled);
        foreach (var listingId in seeded.ListingIds)
            (await AvailableAsync(listingId)).Should().Be(InitialStock);
        (await DbContext.Coupon.AsNoTracking().SingleAsync(c => c.CouponId == couponId)).UsedCount.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmRefund_OnRefundPendingPayment_ConfirmsTheRefund_WithoutReversingTheOrderAgain()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paid = await PayAsync(seeded);
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        await OrderService.CancelOrderAsync(seeded.OrderId);
        var requestedAt = ClockNow;
        Factory.Clock.Advance(TimeSpan.FromHours(3));

        SetCurrentUser(1, "Admin");

        // Batch 42 (FIN-12): «refund» روی پرداختِ در انتظار برگشت دیگر تأیید نمی‌کند (409)؛ تأیید مسیر خودش را دارد.
        var request = () => PaymentService.RefundPaymentAsync(paid.PaymentId, "returned by bank transfer");
        (await request.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PAYMENT_NOT_REFUNDABLE");

        var dto = await PaymentService.ConfirmRefundAsync(
            paid.PaymentId,
            new ConfirmRefundRequest { BankReference = "BANK-B41-1", Note = "returned by bank transfer" });

        dto.State.Should().Be(PaymentState.Refunded);
        DbContext.ChangeTracker.Clear();

        var payment = await PaymentAsync(paid.PaymentId);
        payment.State.Should().Be(PaymentState.Refunded);
        payment.RefundRequestedAtUtc.Should().Be(requestedAt);
        payment.RefundedAtUtc.Should().Be(ClockNow);
        payment.RefundReference.Should().Be("BANK-B41-1");

        (await AvailableAsync(seeded.ListingIds[0])).Should().Be(InitialStock, "the cancel already released the stock; the confirmation must not release it again");
        (await AuditDetailsAsync(nameof(Order), seeded.OrderId, "Order.Cancelled")).Should().ContainSingle();
        (await AuditDetailsAsync(nameof(Payment), paid.PaymentId, "Payment.Refunded")).Should().ContainSingle()
            .Which.Should().Contain("MONEY_MOVED_MANUALLY=true").And.Contain("returned by bank transfer");

        var again = () => PaymentService.ConfirmRefundAsync(paid.PaymentId, new ConfirmRefundRequest { BankReference = "BANK-B41-2" });
        (await again.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PAYMENT_NOT_REFUND_PENDING");
    }

    [Fact]
    public async Task AdminRefund_DeliveredOrder_ReturnsTheGroups_CancelsThePendingPayout_AndReleasesStock()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paid = await PayAsync(seeded);
        await ShipAsync(seeded, 0);
        await DeliverAsync(seeded, 0);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Delivered);

        SetCurrentUser(1, "Admin");
        await PaymentService.RefundPaymentAsync(paid.PaymentId, "customer returned the item");

        DbContext.ChangeTracker.Clear();
        // Batch 42 (FIN-12): درخواست برگشت وجه ⇒ RefundPending (Refunded فقط پس از تأیید بانکی).
        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.RefundPending);
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Returned);
        (await GroupStatusAsync(seeded.Groups[0].GroupId)).Should().Be(SellerGroupStatus.Returned);
        (await PayoutsAsync(seeded.OrderId)).Should().ContainSingle().Which.Status.Should().Be(PayoutStatus.Cancelled);
        (await AvailableAsync(seeded.ListingIds[0])).Should().Be(InitialStock);
    }

    [Fact]
    public async Task AdminRefund_OfADuplicateCapture_LeavesThePaidOrderAlone()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paid = await PayAsync(seeded);
        var duplicateId = await AddPaymentAsync(seeded.OrderId, PaymentState.Succeeded);

        SetCurrentUser(1, "Admin");
        var dto = await PaymentService.RefundPaymentAsync(duplicateId, "charged twice");

        // Batch 42 (FIN-12): درخواست برگشت وجه ⇒ RefundPending (Refunded فقط پس از تأیید بانکی).
        dto.State.Should().Be(PaymentState.RefundPending);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(duplicateId)).State.Should().Be(PaymentState.RefundPending);
        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.Succeeded, "the order is still paid by the first payment");
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Processing);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Processing);
        (await AvailableAsync(seeded.ListingIds[0])).Should().Be(InitialStock - 1);
    }

    // ==================== FIN-01: مرجوعی ادمین ====================

    [Fact]
    public async Task AdminReturn_DeliveredPaidOrder_MovesPaymentToRefundPending_AndCancelsThePendingPayout()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var paid = await PayAsync(seeded);
        await ShipAsync(seeded, 0);
        await ShipAsync(seeded, 1);
        SetCurrentUser(1, "Admin");
        await OrderService.MarkDeliveredAsync(seeded.OrderId);

        var result = await OrderService.MarkReturnedAsync(seeded.OrderId);

        result.Status.Should().Be(nameof(OrderStatus.Returned));
        DbContext.ChangeTracker.Clear();

        var payment = await PaymentAsync(paid.PaymentId);
        payment.State.Should().Be(PaymentState.RefundPending, "a return owes the customer their money");
        payment.RefundRequestedAtUtc.Should().Be(ClockNow);
        payment.RefundedAtUtc.Should().BeNull();

        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Returned);
        (await PayoutsAsync(seeded.OrderId)).Should().HaveCount(2).And.OnlyContain(p => p.Status == PayoutStatus.Cancelled);
        foreach (var listingId in seeded.ListingIds)
            (await AvailableAsync(listingId)).Should().Be(InitialStock);
        (await AuditDetailsAsync(nameof(Order), seeded.OrderId, "Order.Returned")).Should().ContainSingle();
    }

    [Fact]
    public async Task AdminReturn_PaidPayout_CreatesExactlyOneClawbackDebt()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        await PayAsync(seeded);
        await ShipAsync(seeded, 0);
        await DeliverAsync(seeded, 0);

        var payout = await DbContext.SellerPayout.AsTracking().SingleAsync(p => p.OrderSellerGroupId == seeded.Groups[0].GroupId);
        payout.MarkPaid("BANK-41", 0m, payout.AvailableAtUtc); // Batch 44: پس از دورهٔ نگه‌داشت
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        SetCurrentUser(1, "Admin");
        await OrderService.MarkReturnedAsync(seeded.OrderId);

        DbContext.ChangeTracker.Clear();
        var debts = await DbContext.SellerDebt.AsNoTracking().Where(d => d.SellerId == seeded.Groups[0].Seller.SellerId).ToListAsync();
        debts.Should().ContainSingle();
        debts[0].OriginalAmount.Should().Be(payout.Amount); // Batch 44: Amount → OriginalAmount
        debts[0].IsSettled.Should().BeFalse();
        debts[0].CreatedAtUtc.Should().Be(ClockNow);
    }

    // ==================== جاب سفارش رهاشده: همان روال ====================

    [Fact]
    public async Task AbandonedCleanup_UsesTheSharedReversal_CancelsOpenPayment_ReleasesStock_AndIsAudited()
    {
        var seeded = await CreateOrderAsync(sellers: 2);
        var couponId = await AttachUsedCouponAsync(seeded.OrderId);
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        await AgeOrderAsync(seeded.OrderId);

        var cancelled = await NewCleanup().RunCleanupAsync();

        cancelled.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Cancelled);
        (await GroupStatusesAsync(seeded.OrderId)).Should().OnlyContain(s => s == SellerGroupStatus.Cancelled);
        (await PaymentAsync(initiation.PaymentId)).State.Should().Be(PaymentState.Cancelled);
        foreach (var listingId in seeded.ListingIds)
            (await AvailableAsync(listingId)).Should().Be(InitialStock);
        (await DbContext.Coupon.AsNoTracking().SingleAsync(c => c.CouponId == couponId)).UsedCount.Should().Be(0);
        (await AuditDetailsAsync(nameof(Order), seeded.OrderId, "Order.Cancelled")).Should().ContainSingle()
            .Which.Should().Contain("kind=AbandonedOrder");
    }

    // ==================== FIN-11: «تأیید آغازشده» از ستون ====================

    [Fact]
    public async Task Verify_Timeout_StampsVerifyStartedAtUtc_AndStillWritesTheAuditEvidence()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        Factory.Clock.Advance(TimeSpan.FromMinutes(2));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));

        var verify = () => PaymentService.VerifyAsync(initiation.Authority!);
        await verify.Should().ThrowAsync<ServiceUnavailableException>();

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(initiation.PaymentId);
        payment.State.Should().Be(PaymentState.Pending);
        payment.VerifyStartedAtUtc.Should().Be(ClockNow);
        (await AuditDetailsAsync(nameof(Payment), initiation.PaymentId, "Payment.VerificationStarted")).Should().ContainSingle();
    }

    [Fact]
    public async Task Sweep_PendingPaymentWhoseVerifyStarted_IsNotCancelled_AndIsFlaggedOnce()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paymentId = await AddPaymentAsync(
            seeded.OrderId, PaymentState.Pending, verifyStartedAtUtc: DateTime.UtcNow.AddHours(-2), lastChangedAtUtc: DateTime.UtcNow.AddHours(-2));

        await GetService<IAbandonedPaymentSweeper>().SweepAsync();
        await GetService<IAbandonedPaymentSweeper>().SweepAsync();

        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.Pending, "the gateway may have taken the money");
        (await AuditDetailsAsync(nameof(Payment), paymentId, "Payment.ReviewRequired")).Should().ContainSingle();
    }

    [Fact]
    public async Task Initiate_WhenAnOpenPaymentsVerifyStarted_Returns409VerificationPending()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paymentId = await AddPaymentAsync(seeded.OrderId, PaymentState.Pending, verifyStartedAtUtc: DateTime.UtcNow);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var act = () => PaymentService.InitiateAsync(seeded.OrderId, null);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PAYMENT_VERIFICATION_PENDING");
        Gateway.InitiateCalls.Should().BeEmpty();
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.Pending);
    }

    [Fact]
    public async Task CustomerCancel_WhenAPaymentsVerifyStarted_Returns409VerificationPending_AndChangesNothing()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paymentId = await AddPaymentAsync(seeded.OrderId, PaymentState.Pending, verifyStartedAtUtc: DateTime.UtcNow);

        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var act = () => OrderService.CancelOrderAsync(seeded.OrderId);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PAYMENT_VERIFICATION_PENDING");
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.Pending);
        (await AvailableAsync(seeded.ListingIds[0])).Should().Be(InitialStock - 1);
    }

    [Fact]
    public async Task AbandonedCleanup_WhenAPaymentsVerifyStarted_SkipsTheOrder_AndFlagsItOnce()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paymentId = await AddPaymentAsync(seeded.OrderId, PaymentState.Pending, verifyStartedAtUtc: DateTime.UtcNow.AddMinutes(-70));
        await AgeOrderAsync(seeded.OrderId);

        var cancelled = await NewCleanup().RunCleanupAsync();
        await NewCleanup().RunCleanupAsync();

        cancelled.Should().Be(0);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(seeded.OrderId)).Should().Be(OrderStatus.Pending);
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.Pending);
        (await AuditDetailsAsync(nameof(Payment), paymentId, "Payment.ReviewRequired")).Should().ContainSingle();
    }

    [Fact]
    public async Task FailureWebhook_WhenThePaymentsVerifyStarted_DoesNotFailThePayment()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paymentId = await AddPaymentAsync(seeded.OrderId, PaymentState.Pending, verifyStartedAtUtc: DateTime.UtcNow);
        var authority = (await PaymentAsync(paymentId)).TransactionId!;

        var payload = $"{{\"authority\":\"{authority}\",\"status\":\"failed\"}}";
        var dto = await PaymentService.HandleWebhookAsync(payload, SignatureValidator.Sign(payload));

        dto.State.Should().Be(PaymentState.Pending);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.Pending);
    }

    // ==================== RefundPending در مسیرهای دیگر ====================

    [Fact]
    public async Task Deliver_ShippedOrderWhoseRefundIsPending_Returns409OrderRefunded_AndCreatesNoPayout()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paid = await PayAsync(seeded);
        await ShipAsync(seeded, 0);
        await ForcePaymentStateAsync(paid.PaymentId, PaymentState.RefundPending);

        SetCurrentUser(seeded.Groups[0].Seller.UserId, "Seller");
        var act = () => DashboardService.DeliverSellerGroupAsync(seeded.OrderId, seeded.Groups[0].GroupId);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("ORDER_REFUNDED");
        DbContext.ChangeTracker.Clear();
        (await GroupStatusAsync(seeded.Groups[0].GroupId)).Should().Be(SellerGroupStatus.Shipped);
        (await PayoutsAsync(seeded.OrderId)).Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateSuccessWebhook_ForARefundPendingPayment_IsIgnored()
    {
        var seeded = await CreateOrderAsync(sellers: 1);
        var paid = await PayAsync(seeded);
        await ForcePaymentStateAsync(paid.PaymentId, PaymentState.RefundPending);
        var verifyCallsBefore = Gateway.VerifyCalls.Count;

        var payload = $"{{\"authority\":\"{paid.Authority}\",\"status\":\"succeeded\"}}";
        var dto = await PaymentService.HandleWebhookAsync(payload, SignatureValidator.Sign(payload));

        dto.State.Should().Be(PaymentState.RefundPending);
        Gateway.VerifyCalls.Should().HaveCount(verifyCallsBefore);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.RefundPending);
    }

    // ==================== helpers ====================

    private sealed record SeededOrder(
        int OrderId,
        int BuyerUserId,
        IReadOnlyList<(Seller Seller, int GroupId)> Groups,
        IReadOnlyList<int> ListingIds);

    private sealed record PaidPayment(int PaymentId, string Authority);

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private AbandonedOrderCleanupService NewCleanup()
        => new(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AbandonedOrderCleanupService>.Instance,
            Factory.Clock);

    private Task<Payment> PaymentAsync(int paymentId)
        => DbContext.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == paymentId);

    private Task<OrderStatus> OrderStatusAsync(int orderId)
        => DbContext.Order.AsNoTracking().Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

    private Task<SellerGroupStatus> GroupStatusAsync(int groupId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderSellerGroupId == groupId).Select(g => g.Status).SingleAsync();

    private Task<List<SellerGroupStatus>> GroupStatusesAsync(int orderId)
        => DbContext.OrderSellerGroup.AsNoTracking()
            .Where(g => g.OrderId == orderId).Select(g => g.Status).ToListAsync();

    private Task<List<SellerPayout>> PayoutsAsync(int orderId)
        => DbContext.SellerPayout.AsNoTracking()
            .Where(p => p.OrderSellerGroup != null && p.OrderSellerGroup.OrderId == orderId).ToListAsync();

    /// <summary>موجودی قابل فروش از دیتابیس (SQLite جمع decimal را در SQL انجام نمی‌دهد، پس در حافظه جمع می‌شود).</summary>
    private async Task<decimal> AvailableAsync(int listingId)
        => (await DbContext.Inventory.AsNoTracking()
                .Where(i => i.ListingId == listingId)
                .Select(i => i.Quantity)
                .ToListAsync())
            .Sum();

    private Task<List<string>> AuditDetailsAsync(string entityName, int entityId, string action)
        => DbContext.AuditLog.AsNoTracking()
            .Where(a => a.Action == action && a.EntityName == entityName && a.EntityId == entityId.ToString())
            .Select(a => a.Details ?? string.Empty)
            .ToListAsync();

    /// <summary>پرداخت واقعی از مسیر درگاه: Initiate + Verify؛ سفارش و گروه‌ها Processing می‌شوند.</summary>
    private async Task<PaidPayment> PayAsync(SeededOrder seeded)
    {
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        var verified = await PaymentService.VerifyAsync(initiation.Authority!);
        verified.State.Should().Be(PaymentState.Succeeded);
        DbContext.ChangeTracker.Clear();
        return new PaidPayment(initiation.PaymentId, initiation.Authority!);
    }

    private async Task ShipAsync(SeededOrder seeded, int groupIndex)
    {
        var (seller, groupId) = seeded.Groups[groupIndex];
        SetCurrentUser(seller.UserId, "Seller");
        await DashboardService.ShipSellerGroupAsync(
            seeded.OrderId,
            groupId,
            new ShipSellerGroupRequest { CarrierName = "Tipax", TrackingNumber = $"TPX-{Guid.NewGuid():N}"[..20] });
        DbContext.ChangeTracker.Clear();
    }

    private async Task DeliverAsync(SeededOrder seeded, int groupIndex)
    {
        var (seller, groupId) = seeded.Groups[groupIndex];
        SetCurrentUser(seller.UserId, "Seller");
        await DashboardService.DeliverSellerGroupAsync(seeded.OrderId, groupId);
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>پرداختی با وضعیت دلخواه برای همان سفارش (شبیه برداشت تکراری یا تأییدِ نیمه‌کاره)؛ بدون رکورد AuditLog.</summary>
    private async Task<int> AddPaymentAsync(
        int orderId,
        PaymentState state,
        DateTime? verifyStartedAtUtc = null,
        DateTime? lastChangedAtUtc = null)
    {
        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == orderId);
        var invoice = await DbContext.Invoice.FirstOrDefaultAsync(i => i.OrderId == orderId);
        if (invoice is null)
        {
            invoice = new Invoice
            {
                OrderId = orderId,
                InvoiceNumber = $"INV-B41-{Guid.NewGuid():N}",
                SubTotalAmount = order.TotalAmount,
                TotalAmount = order.TotalAmount
            };
            DbContext.Invoice.Add(invoice);
            await DbContext.SaveChangesAsync();
        }

        var payment = new Payment
        {
            Amount = order.TotalAmount,
            OrderId = orderId,
            InvoiceId = invoice.InvoiceId,
            Gateway = PaymentGatewayType.Manual,
            TransactionId = $"FAKE-b41-{Guid.NewGuid():N}",
            State = state,
            LastStateChangedAtUtc = lastChangedAtUtc ?? DateTime.UtcNow,
            VerifyStartedAtUtc = verifyStartedAtUtc
        };
        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return payment.PaymentId;
    }

    private async Task ForcePaymentStateAsync(int paymentId, PaymentState state)
    {
        var payment = await DbContext.Payment.AsTracking().SingleAsync(p => p.PaymentId == paymentId);
        DbContext.Entry(payment).Property(p => p.State).CurrentValue = state;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>کوپنی که یک بار (برای همین سفارش) مصرف شده است.</summary>
    private async Task<int> AttachUsedCouponAsync(int orderId)
    {
        var coupon = new Coupon
        {
            Code = $"B41-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            DiscountPercentage = 10m,
            UsageLimit = 1,
            UsedCount = 1,
            IsActive = true
        };
        DbContext.Coupon.Add(coupon);
        await DbContext.SaveChangesAsync();

        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        order.CouponId = coupon.CouponId;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return coupon.CouponId;
    }

    private async Task AgeOrderAsync(int orderId)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        order.OrderDate = ClockNow.AddMinutes(-75);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<SeededOrder> CreateOrderAsync(int sellers)
    {
        var listings = new List<(Seller Seller, int ListingId)>();
        for (var i = 0; i < sellers; i++)
        {
            var (sellerUser, _) = await CreateUserAsync();
            listings.Add(await CreateSellerWithListingAsync(sellerUser, 100_000m * (i + 1)));
        }

        var (buyerUser, buyer) = await CreateUserAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "B41 Buyer", "Tehran", "09120000000");
        foreach (var (_, listingId) in listings)
            await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1 });

        DbContext.ChangeTracker.Clear();

        var groups = await DbContext.OrderSellerGroup.AsNoTracking().Where(g => g.OrderId == order.OrderId).ToListAsync();

        return new SeededOrder(
            order.OrderId,
            buyerUser.UserId,
            listings.Select(l => (l.Seller, groups.Single(g => g.SellerId == l.Seller.SellerId).OrderSellerGroupId)).ToList(),
            listings.Select(l => l.ListingId).ToList());
    }

    private async Task<(User User, Customer Customer)> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "B41",
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

    private async Task<(Seller Seller, int ListingId)> CreateSellerWithListingAsync(User sellerUser, decimal price)
    {
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B41Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B41-{Guid.NewGuid():N}",
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
            RecipientFirstName = "B41",
            RecipientLastName = "Shop",
            UserId = sellerUser.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, InitialStock));
        await DbContext.SaveChangesAsync();

        return (seller, listing.ListingId);
    }
}
