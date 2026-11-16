using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 43 (FIN-05, FIN-07) — تطبیق پرداخت‌های «پرداخت‌شده ولی تأییدنشده» با درگاه.
///
///  • جاب پرداخت‌های رهاشده پیش از لغو از درگاه استعلام می‌کند: پرداخت‌شده و سفارش قابل پرداخت ⇒ برداشت از مسیر Verify
///    (source = reconciliation)؛ پرداخت‌نشده ⇒ لغو؛ نامعلوم ⇒ Pending می‌ماند و یک بار به صف بررسی می‌رود.
///  • پول هرگز بی‌صدا گم نمی‌شود: پولی که درگاه برداشته Succeeded ثبت می‌شود و با refundNeeded=true به صف بررسی می‌رود؛
///    پرداخت‌شدهٔ تأییدنشده برای سفارش ناپرداختنی بدون Verify لغو می‌شود (درگاه پول را برمی‌گرداند) و علامت می‌خورد.
///  • <c>POST payments/verify-by-authority</c> همان تطبیق را برای یک Authority انجام می‌دهد (فقط ادمین، با AuditLog).
///  • Verify کارمزد و کارت ماسک‌شده را ذخیره می‌کند؛ فقط ادمین آن‌ها را می‌بیند.
/// </summary>
public class Phase15Batch43ReconciliationTests : ControllerIntegrationTestBase
{
    private const string VerifyByAuthorityUrl = "/api/v1/payments/verify-by-authority";

    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private IAbandonedPaymentSweeper Sweeper => GetService<IAbandonedPaymentSweeper>();
    private AppDbContext DbContext => GetService<AppDbContext>();
    private ScriptedPaymentGateway Gateway => Factory.PaymentGateway;

    public Phase15Batch43ReconciliationTests(IntegrationTestFactory<Program> factory) : base(factory)
    {
        Factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    // ==================== جاب: پرداخت‌شده و سفارش قابل پرداخت ====================

    [Fact]
    public async Task Sweep_StalePendingPayment_ThatTheGatewayReportsPaid_IsCaptured_AndTheOrderIsProcessing()
    {
        var open = await CreateStalePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Verified("REF-4301") with
        {
            Fee = 12_000m,
            FeeType = "Merchant",
            CardPanMasked = "502229******5995"
        });

        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(0, "a paid payment is captured, not cancelled");
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Succeeded, "the payer paid; closing the browser tab must not lose the order");
        payment.GatewayReference.Should().Be("REF-4301");
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Processing);
        Gateway.VerifyCalls.Should().ContainSingle().Which.Should().Be((open.Authority, payment.Amount));
        (await AuditDetailsAsync(open.PaymentId, "Payment.VerificationStarted")).Should().ContainSingle()
            .Which.Should().Contain("source=reconciliation");
        payment.ReviewRequiredAtUtc.Should().BeNull("a clean capture needs no manual review");
    }

    [Fact]
    public async Task Sweep_StalePendingPayment_ThatTheGatewayAlreadyVerified_IsRecordedSucceeded()
    {
        var open = await CreateStalePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.Verified("VERIFIED"));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.AlreadyVerified("REF-4302"));

        await Sweeper.SweepAsync();

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Succeeded);
        payment.GatewayReference.Should().Be("REF-4302");
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Processing);
    }

    // ==================== جاب: پرداخت‌نشده ====================

    [Fact]
    public async Task Sweep_StalePendingPayment_ThatTheGatewayReportsNotPaid_IsCancelledAsBefore_AfterAskingTheGateway()
    {
        var open = await CreateStalePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.NotPaid("FAILED"));

        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(open.PaymentId)).State.Should().Be(PaymentState.Cancelled);
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Pending, "the order stays so the buyer can pay again");
        Gateway.InquiryCalls.Should().Contain(open.Authority, "the gateway is asked before a stale payment is cancelled");
        Gateway.VerifyCalls.Should().BeEmpty();
        (await AuditDetailsAsync(open.PaymentId, "Payment.Cancelled")).Should().ContainSingle()
            .Which.Should().Contain("abandoned").And.Contain("gateway=NotPaid");
        (await PaymentAsync(open.PaymentId)).ReviewRequiredAtUtc.Should().BeNull();
    }

    // ==================== جاب: پاسخ نامعلوم ====================

    [Fact]
    public async Task Sweep_WhenTheGatewayAnswerIsUnknown_LeavesThePaymentPending_AndFlagsItOnce()
    {
        var open = await CreateStalePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.Unknown("GATEWAY_UNAVAILABLE", "down"));

        var first = await Sweeper.SweepAsync();
        var second = await Sweeper.SweepAsync();

        (first + second).Should().Be(0);
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Pending, "an unknown answer is not 'not paid'");
        payment.IsReviewOpen.Should().BeTrue();
        payment.ReviewNote.Should().Contain("no definitive answer");
        (await AuditDetailsAsync(open.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle()
            .Which.Should().Contain("gateway=Unknown").And.Contain("gatewayError=GATEWAY_UNAVAILABLE");
        Gateway.InquiryCalls.Count(a => a == open.Authority).Should().Be(2, "the gateway is asked again on every run");
    }

    [Fact]
    public async Task Sweep_UnknownFirst_ThenNotPaid_CancelsThePayment_AndClosesItsOwnWaitingFlag()
    {
        var open = await CreateStalePendingPaymentAsync();
        Gateway.EnqueueInquiryResult(PaymentInquiryResult.Unknown("GATEWAY_UNAVAILABLE", "down"));
        await Sweeper.SweepAsync();
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(open.PaymentId)).IsReviewOpen.Should().BeTrue();

        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.NotPaid("FAILED"));
        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Cancelled);
        payment.IsReviewOpen.Should().BeFalse("a gateway outage must not leave resolved rows in the review queue");
        payment.ReviewNote.Should().Contain("closed automatically by reconciliation");
    }

    [Fact]
    public async Task Sweep_WhenTheGatewayKeepsAnsweringUnknown_StopsAskingAfterFiveInARow()
    {
        var stale = new List<OpenPayment>();
        for (var i = 0; i < 7; i++)
            stale.Add(await CreateStalePendingPaymentAsync());
        foreach (var open in stale)
            Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.Unknown("GATEWAY_UNAVAILABLE", "down"));

        await Sweeper.SweepAsync();

        Gateway.InquiryCalls.Should().HaveCount(5, "a gateway that is down is not called once per stale payment");
        DbContext.ChangeTracker.Clear();
        var ids = stale.Select(s => s.PaymentId).ToList();
        var payments = await DbContext.Payment.AsNoTracking().Where(p => ids.Contains(p.PaymentId)).ToListAsync();
        payments.Should().OnlyContain(p => p.State == PaymentState.Pending);
        payments.Count(p => p.ReviewRequiredAtUtc != null).Should().Be(5);
    }

    // ==================== جاب: پرداخت‌شده ولی سفارش دیگر قابل پرداخت نیست ====================

    [Fact]
    public async Task Sweep_PaidButNotVerified_ForACancelledOrder_IsCancelledWithoutVerify_AndFlaggedForReview()
    {
        var open = await CreateStalePendingPaymentAsync();
        await ForceOrderStatusAsync(open.OrderId, OrderStatus.Cancelled);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));

        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Cancelled);
        Gateway.VerifyCalls.Should().BeEmpty("verifying would capture money for an order that will not be fulfilled");
        payment.IsReviewOpen.Should().BeTrue("somebody has to confirm that the gateway returned the money");
        payment.ReviewNote.Should().Contain("NOT verified").And.Contain("ORDER_NOT_PAYABLE").And.Contain("moneyCaptured=false");
        payment.ReviewNote.Should().NotContain("refundNeeded=true", "no money was captured, so there is nothing to refund by hand");
        (await AuditDetailsAsync(open.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle();
        (await AuditDetailsAsync(open.PaymentId, "Payment.NotVerified")).Should().ContainSingle()
            .Which.Should().Contain("source=reconciliation");
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Sweep_AlreadyVerifiedAtTheGateway_ForACancelledOrder_IsRecordedSucceeded_AndFlaggedRefundNeeded()
    {
        var open = await CreateStalePendingPaymentAsync();
        await ForceOrderStatusAsync(open.OrderId, OrderStatus.Cancelled);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.Verified("VERIFIED"));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.AlreadyVerified("REF-4303"));

        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(0);
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Succeeded, "money the gateway already captured must never be recorded as cancelled");
        payment.GatewayReference.Should().Be("REF-4303");
        payment.IsReviewOpen.Should().BeTrue();
        payment.ReviewNote.Should().Contain("order was Cancelled").And.Contain("refundNeeded=true");
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Cancelled, "a captured payment does not revive a cancelled order");
    }

    [Fact]
    public async Task Sweep_PaidButNotVerified_WhenTheOrderIsAlreadyPaidByAnotherPayment_IsCancelledWithoutVerify_AndFlagged()
    {
        var open = await CreateStalePendingPaymentAsync();
        await AddPaymentAsync(open.OrderId, PaymentState.Succeeded);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));

        await Sweeper.SweepAsync();

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Cancelled);
        Gateway.VerifyCalls.Should().BeEmpty("a second capture for a paid order would need a manual refund");
        payment.ReviewNote.Should().Contain("ORDER_ALREADY_PAID");
    }

    // ==================== جاب: پرداختی که Verify آن قبلاً آغاز شده ====================

    [Fact]
    public async Task Sweep_VerifyStartedWithUnknownOutcome_ThatTheGatewayReportsVerified_IsCaptured_AndItsWaitingFlagIsClosed()
    {
        var open = await CreateStalePendingPaymentAsync();
        SetCurrentUser(open.BuyerUserId, "Customer");
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));
        var verify = () => PaymentService.VerifyAsync(open.Authority);
        await verify.Should().ThrowAsync<Application.Exceptions.ServiceUnavailableException>();

        // اجرای اول: درگاه هنوز پاسخ قطعی نمی‌دهد ⇒ مثل قبل Pending می‌ماند و یک بار علامت می‌خورد.
        Gateway.EnqueueInquiryResult(PaymentInquiryResult.Unknown("GATEWAY_UNAVAILABLE", "down"));
        await Sweeper.SweepAsync();
        DbContext.ChangeTracker.Clear();
        var waiting = await PaymentAsync(open.PaymentId);
        waiting.State.Should().Be(PaymentState.Pending);
        waiting.ReviewNote.Should().Contain("verification outcome is unknown");

        // اجرای دوم: درگاه می‌گوید پول برداشته شده است.
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.Verified("VERIFIED"));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.AlreadyVerified("REF-4304"));
        await Sweeper.SweepAsync();

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Succeeded);
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Processing);
        payment.IsReviewOpen.Should().BeFalse("the question the flag asked has been answered by the gateway");
    }

    [Fact]
    public async Task Sweep_VerifyStarted_PaidNotVerified_ForACancelledOrder_IsNeitherVerifiedNorCancelled_AndIsFlagged()
    {
        var open = await CreateStalePendingPaymentAsync(verifyStarted: true);
        await ForceOrderStatusAsync(open.OrderId, OrderStatus.Cancelled);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));

        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(0);
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Pending, "a payment whose verify started is never cancelled by the job");
        Gateway.VerifyCalls.Should().BeEmpty("the gateway says it is not verified; verifying now would capture money for a cancelled order");
        payment.IsReviewOpen.Should().BeTrue();
    }

    // ==================== جاب: رقابت با Verify خریدار ====================

    [Fact]
    public async Task Sweep_WhenThePaymentChangesWhileTheGatewayIsAsked_DoesNotCancelIt()
    {
        var open = await CreateStalePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.NotPaid("IN_BANK"));

        // در فاصلهٔ استعلام، درخواست دیگری (Verify خریدار) همین پرداخت را «ادعا» می‌کند: RowVersion عوض می‌شود.
        Gateway.BeforeInquiryReturns = async _ =>
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rival = await db.Payment.AsTracking().SingleAsync(p => p.PaymentId == open.PaymentId);
            rival.VerifyStartedAtUtc = DateTime.UtcNow;
            rival.RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 43 };
            await db.SaveChangesAsync();
        };

        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(0);
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Pending, "the buyer's own verify won the race; the job must not cancel under it");
        payment.VerifyStartedAtUtc.Should().NotBeNull();
    }

    // ==================== اقدام ادمین ====================

    [Fact]
    public async Task VerifyByAuthority_ForAPaidUnverifiedPayment_CapturesIt_AndIsAudited()
    {
        var open = await CreatePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Verified("REF-4310") with
        {
            Fee = 5_000m,
            FeeType = "Merchant",
            CardPanMasked = "603799******1234"
        });
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("outcome").GetString().Should().Be("Captured");
        body.RootElement.GetProperty("gatewayStatus").GetString().Should().Be("PaidNotVerified");
        body.RootElement.GetProperty("reviewRequired").GetBoolean().Should().BeFalse();
        var paymentJson = body.RootElement.GetProperty("payment");
        paymentJson.GetProperty("stateName").GetString().Should().Be("Succeeded");
        paymentJson.GetProperty("cardPanMasked").GetString().Should().Be("603799******1234", "an admin sees the gateway details");
        paymentJson.GetProperty("gatewayFee").GetDecimal().Should().Be(5_000m);

        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(open.PaymentId)).State.Should().Be(PaymentState.Succeeded);
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Processing);
        (await AuditDetailsAsync(open.PaymentId, "Payment.VerificationStarted")).Should().ContainSingle()
            .Which.Should().Contain("source=admin verify-by-authority");
        (await AuditDetailsAsync(open.PaymentId, "Payment.VerifyByAuthority")).Should().ContainSingle()
            .Which.Should().Contain("outcome=Captured").And.Contain("gateway=PaidNotVerified");
    }

    [Fact]
    public async Task VerifyByAuthority_WhenTheGatewayReportsNotPaid_LeavesAFreshPaymentPending()
    {
        var open = await CreatePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.NotPaid("IN_BANK"));
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("outcome").GetString().Should().Be("LeftPending");
        body.RootElement.GetProperty("gatewayRawStatus").GetString().Should().Be("IN_BANK");
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Pending, "the payer may still be on the bank page; only the job cancels abandoned payments");
        payment.ReviewRequiredAtUtc.Should().BeNull();
        (await AuditDetailsAsync(open.PaymentId, "Payment.VerifyByAuthority")).Should().ContainSingle();
    }

    [Fact]
    public async Task VerifyByAuthority_ForACancelledPayment_ThatTheGatewayReportsVerified_FlagsRefundNeeded_Once()
    {
        var open = await CreateStalePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.NotPaid("IN_BANK"));
        (await Sweeper.SweepAsync()).Should().Be(1);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.Verified("VERIFIED"));
        var admin = await AdminClientAsync();

        var first = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });
        var second = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("outcome").GetString().Should().Be("NoChange");
        body.RootElement.GetProperty("reviewRequired").GetBoolean().Should().BeTrue();

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Cancelled, "a closed payment is never reopened; the captured money goes to the review queue");
        payment.IsReviewOpen.Should().BeTrue();
        payment.ReviewNote.Should().Contain("money captured").And.Contain("refundNeeded=true");
        (await AuditDetailsAsync(open.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle("a second click must not add the same issue again");
        Gateway.VerifyCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyByAuthority_ForAnUnknownAuthority_Returns404()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = "A-does-not-exist" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be("PAYMENT_NOT_FOUND");
    }

    [Fact]
    public async Task VerifyByAuthority_WithoutAuthority_Returns422()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = "  " });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task VerifyByAuthority_IsAdminOnly()
    {
        var open = await CreatePendingPaymentAsync();
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));

        var anonymous = await Factory.CreateClient().PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });
        var customer = await (await CustomerClientAsync()).PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        customer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(open.PaymentId)).State.Should().Be(PaymentState.Pending);
        Gateway.InquiryCalls.Should().BeEmpty();
    }

    // ==================== N2: استعلام خاموش ====================
    //
    // Batch 43b (صف مالک ۲۰۲۶-۱۱-۱۶): زرین‌پال فقط با Verify خودِ ما پول برداشت می‌کند. پرداخت کهنه‌ای که Verify آن هرگز
    // آغاز نشده (VerifyStartedAtUtc = null) نمی‌تواند برداشت شده باشد؛ با استعلام خاموش مثل پیش از Batch 43 لغو می‌شود
    // (پول تأییدنشده را درگاه به خریدار برمی‌گرداند). فقط پرداختی که Verify آن آغاز شده و نتیجه‌اش نامعلوم است به صف
    // بررسی می‌رود. با استعلام روشن رفتار Batch 43 عوض نمی‌شود.

    [Fact]
    public async Task Sweep_WithInquiryOff_CancelsEveryStalePaymentWhoseVerifyNeverStarted_WithoutQueueingOrVerifying()
    {
        var stale = new List<OpenPayment>();
        for (var i = 0; i < 6; i++)
            stale.Add(await CreateStalePendingPaymentAsync());
        Gateway.InquiryTurnedOff = true;

        var first = await Sweeper.SweepAsync();
        var second = await Sweeper.SweepAsync();

        first.Should().Be(6, "Zarinpal captures only on our Verify, so a never-verified payment holds no money of ours; " +
                             "every one is cancelled, not only the first five (an inquiry that is off is not a gateway outage)");
        second.Should().Be(0);
        Gateway.VerifyCalls.Should().BeEmpty("the job never verifies (captures) on its own without an inquiry");
        DbContext.ChangeTracker.Clear();
        foreach (var open in stale)
        {
            var payment = await PaymentAsync(open.PaymentId);
            payment.State.Should().Be(PaymentState.Cancelled);
            payment.ReviewRequiredAtUtc.Should().BeNull("a never-verified stale payment does not belong in the manual review queue");
            (await AuditDetailsAsync(open.PaymentId, "Payment.ReviewRequired")).Should().BeEmpty();
            (await AuditDetailsAsync(open.PaymentId, "Payment.Cancelled")).Should().ContainSingle()
                .Which.Should().Contain("abandoned").And.Contain("gatewayError=INQUIRY_DISABLED")
                .And.Contain("verification never started");
            (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Pending, "the order stays so the buyer can pay again (rule unchanged)");
        }
    }

    [Fact]
    public async Task Sweep_WithInquiryOff_QueuesOnlyTheStalePaymentWhoseVerifyStarted_AndNeverCancelsIt()
    {
        var neverVerified = await CreateStalePendingPaymentAsync();
        var verifyStarted = await CreateStalePendingPaymentAsync(verifyStarted: true);
        Gateway.InquiryTurnedOff = true;

        var first = await Sweeper.SweepAsync();
        var second = await Sweeper.SweepAsync();

        (first + second).Should().Be(1, "only the never-verified payment is cancelled");
        Gateway.VerifyCalls.Should().BeEmpty();
        DbContext.ChangeTracker.Clear();

        var started = await PaymentAsync(verifyStarted.PaymentId);
        started.State.Should().Be(PaymentState.Pending, "a verify that started may have captured the money; it is never cancelled blindly");
        started.IsReviewOpen.Should().BeTrue();
        started.ReviewNote.Should().Contain("verification outcome is unknown");
        (await AuditDetailsAsync(verifyStarted.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle("it is queued once")
            .Which.Should().Contain("gatewayError=INQUIRY_DISABLED");
        (await AuditDetailsAsync(verifyStarted.PaymentId, "Payment.Cancelled")).Should().BeEmpty();

        var never = await PaymentAsync(neverVerified.PaymentId);
        never.State.Should().Be(PaymentState.Cancelled);
        never.ReviewRequiredAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Sweep_WithInquiryOn_NeverVerifiedStalePayments_StillFollowTheGatewayAnswer_AsInBatch43()
    {
        var unknown = await CreateStalePendingPaymentAsync();
        var notPaid = await CreateStalePendingPaymentAsync();
        var paid = await CreateStalePendingPaymentAsync();
        Gateway.SetInquiryResult(unknown.Authority, PaymentInquiryResult.Unknown("GATEWAY_UNAVAILABLE", "down"));
        Gateway.SetInquiryResult(notPaid.Authority, PaymentInquiryResult.NotPaid("FAILED"));
        Gateway.SetInquiryResult(paid.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Verified("REF-43B1"));

        var cancelled = await Sweeper.SweepAsync();

        cancelled.Should().Be(1);
        Gateway.InquiryCalls.Should().Contain(new[] { unknown.Authority, notPaid.Authority, paid.Authority });
        Gateway.VerifyCalls.Select(c => c.Authority).Should().Equal(paid.Authority);
        DbContext.ChangeTracker.Clear();

        var waiting = await PaymentAsync(unknown.PaymentId);
        waiting.State.Should().Be(PaymentState.Pending, "with the inquiry on, an unknown answer is still not 'not paid'");
        waiting.IsReviewOpen.Should().BeTrue();
        waiting.ReviewNote.Should().Contain("no definitive answer");

        var unpaid = await PaymentAsync(notPaid.PaymentId);
        unpaid.State.Should().Be(PaymentState.Cancelled);
        unpaid.ReviewRequiredAtUtc.Should().BeNull();
        (await AuditDetailsAsync(notPaid.PaymentId, "Payment.Cancelled")).Should().ContainSingle()
            .Which.Should().Contain("gateway=NotPaid").And.NotContain("verification never started");

        (await PaymentAsync(paid.PaymentId)).State.Should().Be(PaymentState.Succeeded);
        (await OrderStatusAsync(paid.OrderId)).Should().Be(OrderStatus.Processing);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task WithInquiryOff_NeverVerifiedPaymentAlreadyWaitingForReview_IsCancelled_AndTheReviewIsClosed(
        bool adminClosedTheReviewFirst, bool byOrderCleanup)
    {
        // پرداختی که پیش از 43b/43c (جاب پرداخت‌ها یا جاب سفارش‌ها) با «استعلام خاموش» علامت خورده و بعد کهنه شده است.
        // نسخه‌های فعلی دیگر این علامت را نمی‌گذارند؛ ردیف موجود هر کدام از دو جاب که اول برسد خودکار بسته می‌شود.
        var open = await CreatePendingPaymentAsync();
        var listingId = await ListingOfAsync(open.OrderId);
        var stockWhileReserved = await StockAsync(listingId);
        await AgeOrderAsync(open.OrderId);
        await FlagAsBefore43cWithInquiryOffAsync(open);
        if (adminClosedTheReviewFirst)
        {
            SetCurrentUser(1, "Admin");
            await PaymentService.ResolveReviewAsync(open.PaymentId, "checked the gateway panel: not paid");
            DbContext.ChangeTracker.Clear();
        }
        await AgePaymentAsync(open.PaymentId);
        Gateway.InquiryTurnedOff = true;

        if (byOrderCleanup)
            (await NewOrderCleanup().RunCleanupAsync()).Should().Be(1, "the abandoned order must not stay Pending for ever");
        else
            (await Sweeper.SweepAsync()).Should().Be(1, "the payment must not stay Pending for ever");

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Cancelled);
        payment.IsReviewOpen.Should().BeFalse();
        if (!adminClosedTheReviewFirst)
            payment.ReviewNote.Should().Contain("closed automatically by reconciliation",
                "the 'inquiry is off' waiting flag answered itself: nothing was captured");
        Gateway.VerifyCalls.Should().BeEmpty();
        var cancelledAudit = (await AuditDetailsAsync(open.PaymentId, "Payment.Cancelled")).Should().ContainSingle().Which;
        cancelledAudit.Should().Contain("abandoned");
        if (!adminClosedTheReviewFirst)
            cancelledAudit.Should().Contain("review was closed automatically");
        if (byOrderCleanup)
        {
            (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Cancelled);
            (await StockAsync(listingId)).Should().Be(stockWhileReserved + 1m);
        }
    }

    [Fact]
    public async Task VerifyByAuthority_WithInquiryOff_RunsVerify_CapturesAPaidPayment_AndClosesTheWaitingFlag()
    {
        // Batch 43b: با استعلام خاموش فقط پرداختِ «Verify آغازشده با نتیجهٔ نامعلوم» در صف بررسی است.
        var open = await CreateStalePendingPaymentAsync(verifyStarted: true);
        Gateway.InquiryTurnedOff = true;
        await Sweeper.SweepAsync();
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(open.PaymentId)).IsReviewOpen.Should().BeTrue();
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Verified("REF-4330"));
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("outcome").GetString().Should().Be("Captured");
        body.RootElement.GetProperty("gatewayStatus").GetString().Should().Be("Unknown");
        body.RootElement.GetProperty("gatewayErrorCode").GetString().Should().Be("INQUIRY_DISABLED");
        body.RootElement.GetProperty("reviewRequired").GetBoolean().Should().BeFalse();
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Succeeded);
        payment.GatewayReference.Should().Be("REF-4330");
        payment.IsReviewOpen.Should().BeFalse();
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public async Task VerifyByAuthority_WithInquiryOff_WhenTheGatewayRejectsVerify_MarksThePaymentFailed_WithoutAReviewFlag()
    {
        var open = await CreatePendingPaymentAsync();
        Gateway.InquiryTurnedOff = true;
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Rejected("GATEWAY_REJECTED", "not paid"));
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("outcome").GetString().Should().Be("Failed");
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Failed);
        payment.ReviewRequiredAtUtc.Should().BeNull("a rejected verify of an unpaid payment needs no manual review");
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task VerifyByAuthority_WithInquiryOff_ForACancelledOrder_DoesNotCallVerify()
    {
        var open = await CreatePendingPaymentAsync();
        await ForceOrderStatusAsync(open.OrderId, OrderStatus.Cancelled);
        Gateway.InquiryTurnedOff = true;
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(VerifyByAuthorityUrl, new { authority = open.Authority });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("outcome").GetString().Should().Be("Cancelled");
        Gateway.VerifyCalls.Should().BeEmpty("verifying would capture money for an order that will not be fulfilled");
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(open.PaymentId)).State.Should().Be(PaymentState.Cancelled);
    }

    // ==================== جاب سفارش‌های رهاشده ====================

    [Fact]
    public async Task OrderCleanup_AbandonedOrder_WhoseOpenPaymentTheGatewayReportsPaid_IsPaid_NotCancelled()
    {
        // سفارش ۷۵ دقیقه‌ای با پرداختی که هنوز به مهلت ۳۰ دقیقه‌ای جاب پرداخت‌ها نرسیده است.
        var open = await CreatePendingPaymentAsync();
        await AgeOrderAsync(open.OrderId);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.PaidNotVerified("PAID"));

        var cancelled = await NewOrderCleanup().RunCleanupAsync();

        cancelled.Should().Be(0);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Processing, "the buyer paid; the order must not be cancelled under them");
        (await PaymentAsync(open.PaymentId)).State.Should().Be(PaymentState.Succeeded);
    }

    [Fact]
    public async Task OrderCleanup_WhenTheGatewayCannotSay_KeepsTheOrder_AndFlagsThePaymentOnce()
    {
        var open = await CreatePendingPaymentAsync();
        await AgeOrderAsync(open.OrderId);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.Unknown("GATEWAY_UNAVAILABLE", "down"));

        var first = await NewOrderCleanup().RunCleanupAsync();
        var second = await NewOrderCleanup().RunCleanupAsync();

        (first + second).Should().Be(0);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Pending, "an order is not cancelled while its payment may have been paid");
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Pending);
        payment.IsReviewOpen.Should().BeTrue();
        (await AuditDetailsAsync(open.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle();
        Gateway.VerifyCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task OrderCleanup_WithInquiryOff_NeverVerifiedPayment_CancelsThePaymentAndTheOrder_AndReleasesTheStock_WithoutAReviewFlag()
    {
        // Batch 43c: پیش‌تر جاب سفارش‌ها با استعلام خاموش پرداخت را علامت می‌زد و سفارش را Pending نگه می‌داشت.
        var open = await CreatePendingPaymentAsync();
        var listingId = await ListingOfAsync(open.OrderId);
        var stockWhileReserved = await StockAsync(listingId);
        await AgeOrderAsync(open.OrderId);
        Gateway.InquiryTurnedOff = true;

        var first = await NewOrderCleanup().RunCleanupAsync();
        var second = await NewOrderCleanup().RunCleanupAsync();

        first.Should().Be(1);
        second.Should().Be(0);
        await AssertCancelledWithoutReviewAsync(open, listingId, stockWhileReserved + 1m);
        (await AuditDetailsAsync(open.PaymentId, "Payment.Cancelled")).Single()
            .Should().Contain("abandoned with its order").And.Contain("gatewayError=INQUIRY_DISABLED")
            .And.Contain("verification never started");
    }

    [Fact]
    public async Task OrderCleanup_WhenTheGatewayReportsNotPaid_CancelsTheOrderAndItsOpenPayment_AsBefore()
    {
        var open = await CreatePendingPaymentAsync();
        await AgeOrderAsync(open.OrderId);
        Gateway.SetInquiryResult(open.Authority, PaymentInquiryResult.NotPaid("IN_BANK"));

        var cancelled = await NewOrderCleanup().RunCleanupAsync();

        cancelled.Should().Be(1);
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Cancelled);
        (await PaymentAsync(open.PaymentId)).State.Should().Be(PaymentState.Cancelled);
        Gateway.InquiryCalls.Should().Contain(open.Authority, "the gateway is asked before an order with an open payment is cancelled");
    }

    // ==================== Batch 43c: هر دو جاب، هر دو ترتیب، با استعلام خاموش ====================
    //
    // صف مالک ۲۰۲۶-۱۱-۱۶: با PaymentGatewaySettings:EnableInquiry=false، checkout رهاشده‌ای که Verify پرداختش هرگز آغاز
    // نشده باید به این برسد: پرداخت Cancelled، سفارش Cancelled، موجودی آزاد، هیچ مورد بازی در صف بررسی — مستقل از اینکه
    // کدام جاب (AbandonedPaymentSweeper / AbandonedOrderCleanupService) اول اجرا شود. ساعت با مهلت‌های پیکربندی‌شده
    // (AbandonedPaymentSettings:TimeoutMinutes و AbandonedOrderCleanupService.OrderTimeout) جلو می‌رود.
    // پرداختی که Verify آن آغاز شده و نتیجه‌اش نامعلوم است در هر دو ترتیب فقط یک بار علامت می‌خورد و هرگز لغو نمی‌شود.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothJobs_WithInquiryOff_NeverVerifiedAbandonedCheckout_EndsCancelled_WithStockReleased_AndNoReviewFlag(
        bool sweeperFirst)
    {
        var open = await CreatePendingPaymentAsync();
        var listingId = await ListingOfAsync(open.OrderId);
        var stockWhileReserved = await StockAsync(listingId);
        Gateway.InquiryTurnedOff = true;

        AdvancePastBothConfiguredTimeouts();
        await RunBothJobsAsync(sweeperFirst);

        await AssertCancelledWithoutReviewAsync(open, listingId, stockWhileReserved + 1m);

        // اجرای بعدی هیچ‌چیز را عوض نمی‌کند (موجودی دوباره آزاد نمی‌شود، علامتی ساخته نمی‌شود).
        await RunBothJobsAsync(sweeperFirst);
        await AssertCancelledWithoutReviewAsync(open, listingId, stockWhileReserved + 1m);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothJobs_WithInquiryOff_VerifyStartedWithUnknownOutcome_IsFlaggedOnce_AndNeverCancelled(bool sweeperFirst)
    {
        var open = await CreatePendingPaymentAsync();
        var listingId = await ListingOfAsync(open.OrderId);
        var stockWhileReserved = await StockAsync(listingId);
        SetCurrentUser(open.BuyerUserId, "Customer");
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));
        var verify = () => PaymentService.VerifyAsync(open.Authority);
        await verify.Should().ThrowAsync<Application.Exceptions.ServiceUnavailableException>();
        DbContext.ChangeTracker.Clear();
        Gateway.InquiryTurnedOff = true;

        AdvancePastBothConfiguredTimeouts();
        await RunBothJobsAsync(sweeperFirst);
        await RunBothJobsAsync(sweeperFirst);

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.VerifyStartedAtUtc.Should().NotBeNull();
        payment.State.Should().Be(PaymentState.Pending, "a verify that started may have captured the money; no job cancels it blindly");
        payment.IsReviewOpen.Should().BeTrue();
        (await AuditDetailsAsync(open.PaymentId, "Payment.ReviewRequired")).Should().ContainSingle("it is queued once, whichever job sees it first");
        (await AuditDetailsAsync(open.PaymentId, "Payment.Cancelled")).Should().BeEmpty();
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Pending, "the order is not cancelled while its payment may have been paid");
        (await StockAsync(listingId)).Should().Be(stockWhileReserved, "the stock stays reserved for an order that may have been paid");
        Gateway.VerifyCalls.Should().ContainSingle("only the buyer's own verify ran; the jobs never verify without an inquiry");
    }

    // ==================== FIN-07: جزئیات درگاه ====================

    [Fact]
    public async Task Verify_StoresTheGatewayFeeAndMaskedCard_OwnerSeesOnlyTheirMaskedCard_AdminSeesTheFeeToo()
    {
        var open = await CreatePendingPaymentAsync();
        SetCurrentUser(open.BuyerUserId, "Customer");
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Verified("REF-4320") with
        {
            Fee = 7_500m,
            FeeType = "Merchant",
            // حتی اگر درگاه شمارهٔ کامل بفرستد، فقط ۶ رقم اول و ۴ رقم آخر ذخیره می‌شود.
            CardPanMasked = "6037991234561234"
        });

        var verified = await PaymentService.VerifyAsync(open.Authority);

        verified.State.Should().Be(PaymentState.Succeeded);
        verified.CardPanMasked.Should().Be("603799******1234", "the payer may see the masked card of their own payment");
        verified.GatewayFee.Should().BeNull("the gateway fee is the shop's internal cost");
        verified.GatewayFeeType.Should().BeNull();

        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.GatewayFee.Should().Be(7_500m);
        payment.GatewayFeeType.Should().Be("Merchant");
        payment.CardPanMasked.Should().Be("603799******1234", "a full card number is never stored");

        // صاحب سفارش: تکرار verify و GET payments/order/{orderId} همان کارت ماسک‌شده را می‌دهند، بدون کارمزد.
        var verifiedAgain = await PaymentService.VerifyAsync(open.Authority);
        verifiedAgain.CardPanMasked.Should().Be("603799******1234");
        verifiedAgain.GatewayFee.Should().BeNull();
        var toOwner = await PaymentService.GetByOrderAsync(open.OrderId);
        toOwner!.CardPanMasked.Should().Be("603799******1234");
        toOwner.GatewayFee.Should().BeNull();
        toOwner.GatewayFeeType.Should().BeNull();

        // مشتری دیگر: هیچ‌چیز، نه از راه سفارش و نه از راه Authority.
        var (strangerUser, _) = await CreateUserAsync();
        SetCurrentUser(strangerUser.UserId, "Customer");
        var byOrder = () => PaymentService.GetByOrderAsync(open.OrderId);
        var byAuthority = () => PaymentService.VerifyAsync(open.Authority);
        (await byOrder.Should().ThrowAsync<Application.Exceptions.ForbiddenException>()).Which.Code.Should().Be("PAYMENT_FORBIDDEN");
        (await byAuthority.Should().ThrowAsync<Application.Exceptions.ForbiddenException>()).Which.Code.Should().Be("PAYMENT_FORBIDDEN");

        // ادمین: کارت ماسک‌شده و کارمزد.
        SetCurrentUser(1, "Admin");
        var toAdmin = await PaymentService.GetByOrderAsync(open.OrderId);
        toAdmin!.CardPanMasked.Should().Be("603799******1234");
        toAdmin.GatewayFee.Should().Be(7_500m);
        toAdmin.GatewayFeeType.Should().Be("Merchant");
    }

    [Fact]
    public async Task Webhook_DoesNotReturnGatewayDetails_ToAnUnidentifiedCaller()
    {
        var open = await CreatePendingPaymentAsync();
        Gateway.EnqueueVerifyResult(PaymentVerificationResult.Verified("REF-4321") with
        {
            Fee = 7_500m,
            FeeType = "Merchant",
            CardPanMasked = "603799******1234"
        });
        // فراخوان webhook کاربر نیست: نه ادمین است و نه مالکیتش ثابت شده.
        SetCurrentUser(open.BuyerUserId, "Customer");
        var payload = $"{{\"authority\":\"{open.Authority}\",\"status\":\"succeeded\"}}";

        var dto = await PaymentService.HandleWebhookAsync(payload, GetService<IPaymentSignatureValidator>().Sign(payload));

        dto.State.Should().Be(PaymentState.Succeeded);
        dto.CardPanMasked.Should().BeNull("only the verified owner and admins see the masked card");
        dto.GatewayFee.Should().BeNull();
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(open.PaymentId)).CardPanMasked.Should().Be("603799******1234");
    }

    // ==================== helpers ====================

    private sealed record OpenPayment(int PaymentId, string Authority, int OrderId, int BuyerUserId);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        SetCurrentUser(1, "Admin");
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());
        return client;
    }

    private async Task<HttpClient> CustomerClientAsync()
    {
        var register = await Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = $"0912{Random.Shared.Next(0, 10_000_000):D7}",
            Password = "Customer@123",
            FirstName = "B43",
            LastName = "Customer",
            DeviceId = "b43-device",
            DateOfBirth = "1370/01/01"
        });
        register.EnsureSuccessStatusCode();
        var token = (await register.Content.ReadFromJsonAsync<AuthResultDto>())!.AccessToken;

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private Task<Payment> PaymentAsync(int paymentId)
        => DbContext.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == paymentId);

    private Task<OrderStatus> OrderStatusAsync(int orderId)
        => DbContext.Order.AsNoTracking().Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

    private Task<List<string>> AuditDetailsAsync(int paymentId, string action)
        => DbContext.AuditLog.AsNoTracking()
            .Where(a => a.Action == action && a.EntityName == nameof(Payment) && a.EntityId == paymentId.ToString())
            .Select(a => a.Details ?? string.Empty)
            .ToListAsync();

    /// <summary>سفارش پرداخت‌نشده با یک پرداخت Pending تازه از مسیر واقعی Initiate.</summary>
    private async Task<OpenPayment> CreatePendingPaymentAsync()
    {
        var (sellerUser, _) = await CreateUserAsync();
        var listingId = await CreateSellerWithListingAsync(sellerUser);

        var (buyerUser, buyer) = await CreateUserAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "B43 Buyer", "Tehran", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1 });
        var initiation = await PaymentService.InitiateAsync(order.OrderId, null);
        DbContext.ChangeTracker.Clear();

        return new OpenPayment(initiation.PaymentId, initiation.Authority!, order.OrderId, buyerUser.UserId);
    }

    /// <summary>همان، ولی پرداخت دو ساعت پیش Pending شده است (از مهلت ۳۰ دقیقه‌ای جاب گذشته).</summary>
    private async Task<OpenPayment> CreateStalePendingPaymentAsync(bool verifyStarted = false)
    {
        var open = await CreatePendingPaymentAsync();
        await AgePaymentAsync(open.PaymentId, verifyStarted);
        return open;
    }

    /// <summary>پرداخت را دو ساعت پیش Pending می‌کند (از مهلت ۳۰ دقیقه‌ای جاب گذشته)؛ در صورت خواست، Verify آغازشده.</summary>
    private async Task AgePaymentAsync(int paymentId, bool verifyStarted = false)
    {
        var payment = await DbContext.Payment.AsTracking().SingleAsync(p => p.PaymentId == paymentId);
        payment.LastStateChangedAtUtc = DateTime.UtcNow.AddHours(-2);
        if (verifyStarted)
            payment.VerifyStartedAtUtc = DateTime.UtcNow.AddHours(-2);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private Online_Store_Application.Services.AbandonedOrderCleanupService NewOrderCleanup()
        => new(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Online_Store_Application.Services.AbandonedOrderCleanupService>.Instance,
            Factory.Clock);

    /// <summary>
    /// Batch 43c: ساعت را از هر دو مهلت پیکربندی‌شده می‌گذراند — مهلت پرداخت رهاشده
    /// (AbandonedPaymentSettings:TimeoutMinutes) و مهلت سفارش رهاشده (AbandonedOrderCleanupService.OrderTimeout).
    /// </summary>
    private void AdvancePastBothConfiguredTimeouts()
    {
        var settings = GetService<Microsoft.Extensions.Options.IOptions<Application.Options.AbandonedPaymentSettings>>().Value;
        var paymentTimeout = TimeSpan.FromMinutes(Math.Max(1, settings.TimeoutMinutes));
        var orderTimeout = Online_Store_Application.Services.AbandonedOrderCleanupService.OrderTimeout;

        Factory.Clock.Advance((paymentTimeout > orderTimeout ? paymentTimeout : orderTimeout) + TimeSpan.FromMinutes(1));
    }

    /// <summary>Batch 43c: هر دو جاب، هر کدام در scope خودش (مثل اجرای واقعی)، به ترتیب خواسته‌شده.</summary>
    private async Task RunBothJobsAsync(bool sweeperFirst)
    {
        if (sweeperFirst)
        {
            await RunSweepInOwnScopeAsync();
            await NewOrderCleanup().RunCleanupAsync();
        }
        else
        {
            await NewOrderCleanup().RunCleanupAsync();
            await RunSweepInOwnScopeAsync();
        }
    }

    private async Task RunSweepInOwnScopeAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAbandonedPaymentSweeper>().SweepAsync();
    }

    private async Task AssertCancelledWithoutReviewAsync(OpenPayment open, int listingId, decimal expectedStock)
    {
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(open.PaymentId);
        payment.State.Should().Be(PaymentState.Cancelled, "a never-verified payment holds no money (Zarinpal captures only on our Verify)");
        payment.ReviewRequiredAtUtc.Should().BeNull("a never-verified abandoned payment does not belong in the manual review queue");
        payment.IsReviewOpen.Should().BeFalse();
        (await AuditDetailsAsync(open.PaymentId, "Payment.ReviewRequired")).Should().BeEmpty();
        (await AuditDetailsAsync(open.PaymentId, "Payment.Cancelled")).Should().ContainSingle();
        (await OrderStatusAsync(open.OrderId)).Should().Be(OrderStatus.Cancelled, "the abandoned order is cancelled whichever job runs first");
        (await StockAsync(listingId)).Should().Be(expectedStock, "the reserved stock is released exactly once");
        Gateway.VerifyCalls.Should().BeEmpty("the jobs never verify (capture) without an inquiry");
    }

    private Task<int> ListingOfAsync(int orderId)
        => DbContext.OrderItem.AsNoTracking().Where(i => i.OrderId == orderId).Select(i => i.ListingId).SingleAsync();

    private async Task<decimal> StockAsync(int listingId)
        // SQLite cannot SUM decimals; the rows are summed on the client.
        => (await DbContext.Inventory.AsNoTracking().Where(i => i.ListingId == listingId).Select(i => i.Quantity).ToListAsync()).Sum();

    /// <summary>
    /// Batch 43c: علامتِ «استعلام خاموش است» را همان‌طور که نسخه‌های پیش از 43c (جاب سفارش‌ها؛ جاب پرداخت‌ها پیش از 43b)
    /// می‌نوشتند، روی پرداخت می‌گذارد. متن عمداً اینجا تکرار شده است: داده‌ای است که نسخهٔ قدیمی نوشته و تغییر متن در کد
    /// باید این آزمون را بشکند، وگرنه آن ردیف‌ها دیگر خودکار بسته نمی‌شوند.
    /// </summary>
    private async Task FlagAsBefore43cWithInquiryOffAsync(OpenPayment open)
    {
        var payment = await DbContext.Payment.AsTracking().SingleAsync(p => p.PaymentId == open.PaymentId);
        var note =
            $"source=abandoned-payment sweep | order={payment.OrderId} | amount={payment.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} " +
            "| paymentState=Pending | issues: the gateway inquiry is turned off (PaymentGatewaySettings:EnableInquiry=false), so it is not known " +
            "whether this stale pending payment was paid; it was neither verified nor cancelled automatically. Check the gateway panel: " +
            "if it was paid run POST payments/verify-by-authority, otherwise cancel the order";
        payment.FlagForReview(DateTime.UtcNow.AddMinutes(-5), note);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>سفارش را ۷۵ دقیقه پیش می‌برد (از مهلت ۶۰ دقیقه‌ای جاب سفارش‌های رهاشده گذشته).</summary>
    private async Task AgeOrderAsync(int orderId)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        order.OrderDate = DateTime.UtcNow.AddMinutes(-75);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task ForceOrderStatusAsync(int orderId, OrderStatus status)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        DbContext.Entry(order).Property(nameof(Order.Status)).CurrentValue = status;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>پرداخت دیگری با وضعیت دلخواه برای همان سفارش (بدون رکورد AuditLog).</summary>
    private async Task<int> AddPaymentAsync(int orderId, PaymentState state)
    {
        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == orderId);
        var invoiceId = await DbContext.Invoice.AsNoTracking()
            .Where(i => i.OrderId == orderId).Select(i => i.InvoiceId).FirstAsync();

        var payment = new Payment
        {
            Amount = order.TotalAmount,
            OrderId = orderId,
            InvoiceId = invoiceId,
            Gateway = PaymentGatewayType.Manual,
            TransactionId = $"FAKE-b43-{Guid.NewGuid():N}",
            State = state,
            LastStateChangedAtUtc = DateTime.UtcNow
        };
        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return payment.PaymentId;
    }

    private async Task<(User User, Customer Customer)> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "B43",
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

    private async Task<int> CreateSellerWithListingAsync(User sellerUser)
    {
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B43Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B43-{Guid.NewGuid():N}",
            Price = 150_000m,
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
            Price = 150_000m,
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
            RecipientFirstName = "B43",
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

/// <summary>
/// Batch 43 — کلید <c>AbandonedPaymentSettings:InquireGatewayBeforeCancel = false</c> رفتار پیش از Batch 43 را برمی‌گرداند
/// (برای وقتی که متد استعلام روی مرچنت فعال نیست): پرداخت رهاشده بدون تماس با درگاه لغو می‌شود.
/// </summary>
public class Phase15Batch43InquiryDisabledTests
    : IClassFixture<Phase15Batch43InquiryDisabledTests.InquiryDisabledFactory>, IAsyncLifetime
{
    public sealed class InquiryDisabledFactory : IntegrationTestFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AbandonedPaymentSettings:InquireGatewayBeforeCancel"] = "false"
            }));
        }
    }

    private readonly InquiryDisabledFactory _factory;

    public Phase15Batch43InquiryDisabledTests(InquiryDisabledFactory factory)
    {
        _factory = factory;
        _factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sweep_WithInquiryTurnedOff_CancelsTheStalePayment_WithoutCallingTheGateway()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            FirstName = "B43",
            LastName = "User",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();
        var customer = new Customer { UserId = user.UserId };
        db.Customer.Add(customer);
        await db.SaveChangesAsync();
        var order = await scope.ServiceProvider.GetRequiredService<IOrderService>()
            .CreateEmptyOrderAsync(customer.CustomerId, "B43 Buyer", "Tehran", "09120000000");

        var invoice = new Invoice
        {
            OrderId = order.OrderId,
            InvoiceNumber = $"INV-B43-{Guid.NewGuid():N}",
            SubTotalAmount = 90_000m,
            TotalAmount = 90_000m
        };
        db.Invoice.Add(invoice);
        await db.SaveChangesAsync();
        var payment = new Payment
        {
            Amount = 90_000m,
            OrderId = order.OrderId,
            InvoiceId = invoice.InvoiceId,
            TransactionId = $"FAKE-b43-{Guid.NewGuid():N}",
            State = PaymentState.Pending,
            LastStateChangedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        db.Payment.Add(payment);
        await db.SaveChangesAsync();
        _factory.PaymentGateway.SetInquiryResult(payment.TransactionId, PaymentInquiryResult.PaidNotVerified("PAID"));

        var cancelled = await scope.ServiceProvider.GetRequiredService<IAbandonedPaymentSweeper>().SweepAsync();

        cancelled.Should().Be(1);
        db.ChangeTracker.Clear();
        (await db.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == payment.PaymentId)).State
            .Should().Be(PaymentState.Cancelled);
        _factory.PaymentGateway.InquiryCalls.Should().BeEmpty();
        _factory.PaymentGateway.VerifyCalls.Should().BeEmpty();
    }
}

/// <summary>
/// Batch 43 (تصمیم باز N2) — درگاه واقعی زرین‌پال با تنظیم پیش‌فرض <c>PaymentGatewaySettings:EnableInquiry = false</c>:
/// جاب هیچ تماسی با زرین‌پال نمی‌گیرد (نه استعلام، نه Verify). Batch 43b: پرداخت کهنه‌ای که Verify آن هرگز آغاز نشده
/// لغو می‌شود و به صف بررسی نمی‌رود (زرین‌پال فقط با Verify ما برداشت می‌کند). با <c>EnableInquiry = true</c> همان جاب
/// از زرین‌پال استعلام می‌کند و پرداختِ انجام‌شده را برداشت می‌کند.
/// </summary>
public class Phase15Batch43ZarinpalInquirySwitchTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);

            var json = url.EndsWith("/inquiry.json", StringComparison.Ordinal)
                ? """{"data":{"status":"PAID","code":100,"message":"Success"},"errors":[]}"""
                : """{"data":{"code":100,"message":"Verified","card_pan":"502229******5995","ref_id":4340,"fee_type":"Merchant","fee":1500},"errors":[]}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ZarinpalFactory : IntegrationTestFactory<Program>
    {
        private readonly bool? _enableInquiry;

        public ZarinpalFactory(bool? enableInquiry) => _enableInquiry = enableInquiry;

        public RecordingHandler Handler { get; } = new();

        protected override bool UseScriptedPaymentGateway => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddScoped<IPaymentGateway>(_ =>
                {
                    var settings = new Application.Options.PaymentGatewaySettings
                    {
                        Provider = "Zarinpal",
                        MerchantId = "00000000-0000-0000-0000-000000000000",
                        EnableRealGateway = true
                    };
                    // null یعنی کلید اصلاً تنظیم نشده است (مقدار پیش‌فرض).
                    if (_enableInquiry is not null)
                        settings.EnableInquiry = _enableInquiry.Value;

                    return new BusinessLogic.Services.Implementations.ZarinpalPaymentGateway(
                        new HttpClient(Handler),
                        Microsoft.Extensions.Options.Options.Create(settings),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<BusinessLogic.Services.Implementations.ZarinpalPaymentGateway>.Instance);
                });
            });
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Sweep_WithZarinpalInquiryOff_ByDefault_CallsNothing_AndCancelsTheNeverVerifiedPayment_WithoutQueueingIt(bool? enableInquiry)
    {
        using var factory = new ZarinpalFactory(enableInquiry);
        var paymentId = await SeedStalePendingPaymentAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<IAbandonedPaymentSweeper>();
        var first = await sweeper.SweepAsync();
        var second = await sweeper.SweepAsync();

        first.Should().Be(1, "Zarinpal captures only on our Verify; a payment never verified holds no money and is cancelled (Batch 43b)");
        second.Should().Be(0);
        factory.Handler.Urls.Should().BeEmpty("with the inquiry off the job must not call Zarinpal at all — neither inquiry nor verify");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var payment = await db.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == paymentId);
        payment.State.Should().Be(PaymentState.Cancelled);
        payment.ReviewRequiredAtUtc.Should().BeNull();
        (await db.AuditLog.AsNoTracking().CountAsync(a => a.Action == "Payment.ReviewRequired" && a.EntityId == paymentId.ToString()))
            .Should().Be(0);
        (await db.AuditLog.AsNoTracking().CountAsync(a => a.Action == "Payment.Cancelled" && a.EntityId == paymentId.ToString()))
            .Should().Be(1);
    }

    [Fact]
    public async Task Sweep_WithZarinpalInquiryOn_AsksInquiryThenVerify_AndCapturesThePaidPayment()
    {
        using var factory = new ZarinpalFactory(enableInquiry: true);
        var paymentId = await SeedStalePendingPaymentAsync(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var cancelled = await scope.ServiceProvider.GetRequiredService<IAbandonedPaymentSweeper>().SweepAsync();

        cancelled.Should().Be(0);
        factory.Handler.Urls.Should().Equal(
            "https://payment.zarinpal.com/pg/v4/payment/inquiry.json",
            "https://payment.zarinpal.com/pg/v4/payment/verify.json");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var payment = await db.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == paymentId);
        payment.State.Should().Be(PaymentState.Succeeded);
        payment.GatewayReference.Should().Be("4340");
        payment.GatewayFee.Should().Be(1_500m);
        payment.GatewayFeeType.Should().Be("Merchant");
        payment.CardPanMasked.Should().Be("502229******5995");
    }

    private static async Task<int> SeedStalePendingPaymentAsync(IntegrationTestFactory<Program> factory)
    {
        await factory.InitializeDatabaseAsync();
        factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            FirstName = "B43",
            LastName = "User",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();
        var customer = new Customer { UserId = user.UserId };
        db.Customer.Add(customer);
        await db.SaveChangesAsync();
        var order = await scope.ServiceProvider.GetRequiredService<IOrderService>()
            .CreateEmptyOrderAsync(customer.CustomerId, "B43 Buyer", "Tehran", "09120000000");

        var invoice = new Invoice
        {
            OrderId = order.OrderId,
            InvoiceNumber = $"INV-B43-{Guid.NewGuid():N}",
            SubTotalAmount = 90_000m,
            TotalAmount = 90_000m
        };
        db.Invoice.Add(invoice);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            Amount = 90_000m,
            OrderId = order.OrderId,
            InvoiceId = invoice.InvoiceId,
            Gateway = PaymentGatewayType.Zarinpal,
            TransactionId = $"A{Guid.NewGuid():N}",
            State = PaymentState.Pending,
            LastStateChangedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        db.Payment.Add(payment);
        await db.SaveChangesAsync();
        return payment.PaymentId;
    }
}

/// <summary>
/// Batch 43 — سقف نرخ <c>POST payments/verify</c> برای هر کاربر (RateLimiting:PaymentVerify، پیش‌فرض ۱۰ در دقیقه).
/// Authority ناموجود عمداً استفاده می‌شود: پاسخ عادی آن 404 است، پس هر 429 یعنی limiter درخواست را رد کرده است.
/// </summary>
public class Phase15Batch43VerifyRateLimitTests : IClassFixture<RateLimitingTestFactory>, IAsyncLifetime
{
    private const string VerifyUrl = "/api/v1/payments/verify?authority=A-b43-unknown";

    private readonly RateLimitingTestFactory _factory;
    private readonly HttpClient _client;

    public Phase15Batch43VerifyRateLimitTests(RateLimitingTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Verify_TheEleventhCallInAMinute_Returns429_WithTheStandardBody_AndOnlyForThatUser()
    {
        var tokenA = await RegisterAsync("09134300001", "203.0.113.61");
        var tokenB = await RegisterAsync("09134300002", "203.0.113.62");
        const string sharedIp = "203.0.113.63";

        for (var i = 0; i < RateLimitingTestFactory.PaymentVerifyLimit; i++)
            (await PostAsync(VerifyUrl, sharedIp, tokenA)).StatusCode
                .Should().Be(HttpStatusCode.NotFound, $"verify {i + 1} is still inside the quota");

        var rejected = await PostAsync(VerifyUrl, sharedIp, tokenA);

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMIT_EXCEEDED");
        body.RootElement.GetProperty("status").GetInt32().Should().Be(429);

        (await PostAsync(VerifyUrl, sharedIp, tokenB)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "one user's quota must not block another user behind the same IP");
    }

    private async Task<string> RegisterAsync(string phone, string peerIp)
    {
        // Batch 46: registration needs a verified phone (two-step flow); the helper adds the verification token.
        var response = await _client.PostRegisterWithVerifiedPhoneAsync(_factory, new
        {
            PhoneNumber = phone,
            Password = "Test@123",
            FirstName = "کاربر",
            LastName = "آزمون",
            DeviceId = "b43-rate-limit"
        }, request => request.Headers.Add(RateLimitingTestFactory.PeerIpHeader, peerIp));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResultDto>())!.AccessToken;
    }

    private Task<HttpResponseMessage> PostAsync(string url, string peerIp, string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add(RateLimitingTestFactory.PeerIpHeader, peerIp);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return _client.SendAsync(request);
    }
}
