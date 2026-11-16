using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Services.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Services;

/// <summary>
/// M7.9 — جاب پس‌زمینه برای لغو خودکار سفارش‌های در انتظار رهاشده و آزادسازی موجودی رزروشده.
/// اجرای هر ۱۰ دقیقه؛ بررسی سفارش‌های Pending با بیش از ۶۰ دقیقه عمر.
/// </summary>
public sealed class AbandonedOrderCleanupService : BackgroundService
{
    private const string ReviewRequiredAction = "Payment.ReviewRequired";

    /// <summary>سفارش Pending پس از این مدت (از OrderDate) رهاشده تلقی و لغو می‌شود.</summary>
    public static readonly TimeSpan OrderTimeout = TimeSpan.FromMinutes(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AbandonedOrderCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    public AbandonedOrderCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AbandonedOrderCleanupService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AbandonedOrderCleanupService started with interval {Interval}.", _interval);

        using var timer = new PeriodicTimer(_interval, _timeProvider);

        do
        {
            await RunCleanupAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Batch 4 (F-18): هر سفارش در scope و SaveChanges جداگانهٔ خودش لغو می‌شود.
    /// پیش‌تر یک سفارش خراب (مثلاً گروهِ «تحویل‌شده» در سفارش Pending) کل دسته را
    /// برمی‌گرداند و هیچ موجودی رزروشده‌ای آزاد نمی‌شد. اکنون آن سفارش ثبت خطا و رد
    /// می‌شود و بقیه پردازش می‌شوند.
    /// </summary>
    /// <returns>تعداد سفارش‌هایی که واقعاً لغو و ذخیره شدند.</returns>
    public async Task<int> RunCleanupAsync(CancellationToken cancellationToken = default)
    {
        List<int> staleOrderIds;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var cutoff = _timeProvider.GetUtcNow().UtcDateTime.Subtract(OrderTimeout);

            staleOrderIds = (await unitOfWork.Repository<Order>()
                    .ListAsync(
                        new Spec<Order>().Where(o => o.Status == OrderStatus.Pending && o.OrderDate < cutoff),
                        cancellationToken))
                .Select(o => o.OrderId)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AbandonedOrderCleanupService could not list abandoned orders.");
            return 0;
        }

        if (staleOrderIds.Count == 0)
            return 0;

        _logger.LogInformation("Found {Count} abandoned pending orders to cancel.", staleOrderIds.Count);

        var cancelled = 0;
        var failed = 0;

        foreach (var orderId in staleOrderIds)
        {
            try
            {
                if (await CancelAbandonedOrderAsync(orderId, cancellationToken))
                    cancelled++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "AbandonedOrderCleanupService could not cancel order {OrderId}; it was skipped and the remaining orders continue.",
                    orderId);
            }
        }

        _logger.LogWarning(
            "AbandonedOrderCleanupService cancelled {Cancelled} abandoned orders and released inventory; {Failed} failed.",
            cancelled, failed);

        return cancelled;
    }

    /// <summary>
    /// یک سفارش رهاشده را در scope خودش لغو و ذخیره می‌کند.
    /// اگر سفارش در این فاصله پرداخت/تغییر کرده باشد، کاری نمی‌کند.
    /// </summary>
    private async Task<bool> CancelAbandonedOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var order = await unitOfWork.Repository<Order>()
            .FirstOrDefaultAsync(
                new Spec<Order>()
                    .Where(o => o.OrderId == orderId)
                    .Include(o => o.OrderItems)
                    .Include(o => o.SellerGroups)
                    .Include(o => o.Payments)
                    .AsTracking(),
                cancellationToken);

        if (order is null || order.Status != OrderStatus.Pending)
            return false;

        // Batch 4c: پرداخت پیش از به‌روزرسانی سفارش Succeeded ذخیره می‌شود (Batch 4b)؛ اگر آن گام
        // شکسته باشد، سفارش با پرداخت موفق Pending می‌ماند. چنین سفارشی هرگز لغو نمی‌شود و موجودی‌اش
        // آزاد نمی‌شود (پولش گرفته شده)؛ فقط برای بررسی دستی ثبت می‌شود.
        var succeededPayment = order.Payments.FirstOrDefault(p => p.State == PaymentState.Succeeded);
        if (succeededPayment is not null)
        {
            await FlagPaidPendingOrderAsync(
                unitOfWork,
                scope.ServiceProvider.GetRequiredService<IAuditService>(),
                order,
                succeededPayment,
                cancellationToken);
            return false;
        }

        // Batch 5 (F-07): Verify یک پرداخت آغاز شده و نتیجه‌اش نامعلوم است (مثلاً timeout)؛ شاید پول برداشته
        // شده باشد. سفارش لغو و موجودی آزاد نمی‌شود؛ فقط یک بار برای بررسی دستی ثبت می‌شود. تأیید دوبارهٔ همان
        // پرداخت (توسط مشتری یا ادمین) وضعیت را قطعی می‌کند و اجرای بعدی جاب بر همان اساس عمل می‌کند.
        // Batch 41 (FIN-11): «تأیید آغازشده» از ستون Payment.VerifyStartedAtUtc خوانده می‌شود، نه از AuditLog.
        var unresolved = PaymentLifecycle.FindUnresolvedVerifications(order.Payments);
        if (unresolved.Count > 0)
        {
            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            foreach (var payment in order.Payments.Where(p => unresolved.Contains(p.PaymentId)))
            {
                await FlagForReviewOnceAsync(
                    unitOfWork,
                    auditService,
                    order,
                    payment,
                    "the payment's verification outcome is unknown (verify started, no definitive gateway answer); cleanup skipped the order — verify the payment again to resolve it",
                    cancellationToken);
            }
            return false;
        }

        // Batch 43 (FIN-05): سفارشِ ۶۰ دقیقه‌ای ممکن است پرداخت بازی داشته باشد که خریدار پولش را داده ولی مرورگرش پیش از
        // Verify بسته شده و هنوز به مهلت جاب پرداخت‌ها نرسیده است. پیش از لغو از درگاه استعلام می‌شود؛ سفارش فقط وقتی
        // لغو می‌شود که درگاه برای همهٔ پرداخت‌های بازش بگوید «پرداخت نشده».
        if (!await OpenPaymentsAreUnpaidAsync(scope.ServiceProvider, order, cancellationToken))
            return false;

        // Batch 41 (FIN-01): همان روال لغو مشتری و ادمین — گروه‌ها، موجودی، پرداخت‌های باز، سهمیهٔ کوپن و AuditLog
        // فقط یک‌جا نوشته شده‌اند. اگر روال رد کند (مثلاً گروهِ «تحویل‌شده» در سفارش Pending) هیچ‌چیز ذخیره نمی‌شود.
        await scope.ServiceProvider.GetRequiredService<IOrderReversalService>().ReverseAsync(
            order,
            OrderReversalKind.AbandonedOrder,
            "abandoned: order cancelled by timeout",
            cancellationToken);

        unitOfWork.Repository<Order>().Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Batch 43 (FIN-05): برای هر پرداخت بازِ سفارش (Pending با Authority) از درگاه استعلام می‌کند
    /// (<see cref="IPaymentService.ReconcileBeforeOrderCancellationAsync"/>). true فقط وقتی که درگاه همه را «پرداخت‌نشده»
    /// گفته و هیچ‌چیز عوض نشده است؛ آنگاه سفارش مثل قبل لغو می‌شود و روال برگرداندن، پرداخت‌های باز را لغو می‌کند.
    /// هر نتیجهٔ دیگر (برداشت شد، پاسخ نامعلوم، رد Verify، تغییر هم‌زمان) ⇒ false: این اجرا سفارش را رها می‌کند و
    /// اجرای بعدی با وضعیت تازه تصمیم می‌گیرد. با <c>AbandonedPaymentSettings:InquireGatewayBeforeCancel = false</c>
    /// استعلام انجام نمی‌شود (رفتار قبلی). Batch 43c: با استعلام خاموش (<c>INQUIRY_DISABLED</c>) پرداختِ هرگز Verify‌نشده
    /// همان‌جا لغو شده و «پرداخت‌نشده» حساب می‌شود (همان قاعدهٔ جاب پرداخت‌ها، مستقل از ترتیب اجرای دو جاب).
    /// </summary>
    private async Task<bool> OpenPaymentsAreUnpaidAsync(
        IServiceProvider services, Order order, CancellationToken cancellationToken)
    {
        var openPaymentIds = order.Payments
            .Where(p => p.State == PaymentState.Pending && !string.IsNullOrWhiteSpace(p.TransactionId))
            .Select(p => p.PaymentId)
            .ToList();

        if (openPaymentIds.Count == 0
            || !services.GetRequiredService<IOptions<AbandonedPaymentSettings>>().Value.InquireGatewayBeforeCancel)
            return true;

        var paymentService = services.GetRequiredService<IPaymentService>();
        var allUnpaid = true;

        foreach (var paymentId in openPaymentIds)
        {
            var verifyStarted = order.Payments.First(p => p.PaymentId == paymentId).VerifyStartedAtUtc is not null;
            var result = await paymentService.ReconcileBeforeOrderCancellationAsync(paymentId, cancellationToken);

            // Batch 43c: با استعلام خاموش، پرداختی که Verify آن هرگز آغاز نشده همان‌جا لغو شده است (زرین‌پال فقط با Verify
            // خودِ ما برداشت می‌کند، پس پولی گرفته نشده). سفارش مثل «پرداخت‌نشده» لغو و موجودی آزاد می‌شود؛ بدون صف بررسی.
            // پرداختی که Verify آن آغاز شده اینجا نمی‌رسد (بالاتر فقط علامت می‌خورد) و تطبیق هم هرگز لغوش نمی‌کند.
            var neverVerifiedWithInquiryOff =
                result.GatewayErrorCode == PaymentInquiryResult.InquiryDisabledCode
                && result.Outcome == nameof(PaymentReconciliationOutcome.Cancelled)
                && !verifyStarted;

            var unpaid = neverVerifiedWithInquiryOff
                         || (result.GatewayStatus == nameof(PaymentInquiryStatus.NotPaid)
                             && result.Outcome == nameof(PaymentReconciliationOutcome.LeftPending));
            if (unpaid)
                continue;

            allUnpaid = false;
            _logger.LogWarning(
                "Abandoned order {OrderId} was not cancelled: payment {PaymentId} reconciled as {Outcome} (gateway {GatewayStatus}).",
                order.OrderId, paymentId, result.Outcome, result.GatewayStatus);
        }

        return allUnpaid;
    }

    /// <summary>
    /// Batch 4c: سفارش Pending با پرداخت Succeeded را به‌جای لغو، با عنوان Payment.ReviewRequired
    /// در AuditLog ثبت می‌کند. جاب هر ۱۰ دقیقه اجرا می‌شود، پس اگر برای همان پرداخت قبلاً
    /// (اینجا یا در مسیر verify/webhook) مورد بررسی دستی ثبت شده باشد، دوباره ثبت نمی‌شود؛
    /// لاگ Error در هر اجرا باقی می‌ماند.
    /// </summary>
    private async Task FlagPaidPendingOrderAsync(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        Order order,
        Payment payment,
        CancellationToken cancellationToken)
    {
        await FlagForReviewOnceAsync(
            unitOfWork,
            auditService,
            order,
            payment,
            "order is still Pending although the payment Succeeded; cleanup skipped it",
            cancellationToken);
    }

    /// <summary>
    /// یک مورد Payment.ReviewRequired برای پرداخت ثبت می‌کند، مگر اینکه قبلاً (اینجا یا در مسیر verify/webhook)
    /// ثبت شده باشد. Batch 5 همین را برای پرداختی که نتیجهٔ تأییدش نامعلوم است به کار می‌برد.
    /// </summary>
    private async Task FlagForReviewOnceAsync(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        Order order,
        Payment payment,
        string issue,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            "Abandoned-order cleanup skipped order {OrderId} (payment {PaymentId}): {Issue}. Needs manual review.",
            order.OrderId, payment.PaymentId, issue);

        // Batch 42 (FIN-12): «یک بار» از ستون خودِ پرداخت خوانده می‌شود، نه از AuditLog؛ پرداخت در صف بررسی
        // (GET payments/review) دیده می‌شود. بررسیِ بسته‌شده دوباره باز نمی‌شود.
        if (payment.ReviewRequiredAtUtc is not null)
            return;

        var details =
            $"source=abandoned-order cleanup | order={order.OrderId} | amount={payment.Amount} " +
            $"| paymentState={payment.State} | issues: {issue}";

        payment.FlagForReview(_timeProvider.GetUtcNow().UtcDateTime, details);
        unitOfWork.Repository<Payment>().Update(payment);

        await auditService.RecordAsync(
            ReviewRequiredAction,
            nameof(Payment),
            payment.PaymentId.ToString(),
            details,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
