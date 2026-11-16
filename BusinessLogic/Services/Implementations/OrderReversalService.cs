using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 41 (FIN-01، FIN-03) — تنها روال «برگرداندن سفارش».
///
/// <para>
/// قواعد (یک بار، برای همهٔ مسیرها):
/// </para>
/// <list type="bullet">
/// <item>سفارش: مسیرهای لغو ⇒ Cancelled؛ مرجوعی ⇒ Returned؛ برگشت وجه ادمین ⇒ تحویل‌شده Returned، پیش از ارسالِ کامل
/// Cancelled، سفارشِ «ارسال‌شده» (همهٔ بسته‌ها در راه) دست نمی‌خورد.</item>
/// <item>بسته (گروه فروشنده): Pending/Processing ⇒ لغو و آزادسازی موجودی؛ Shipped ⇒ لغو <b>بدون</b> آزادسازی موجودی
/// (کالا در راه است، نه در انبار)؛ Delivered ⇒ فقط در مرجوعی/برگشت وجه، Returned و آزادسازی موجودی.</item>
/// <item>تسویه: Pending لغو می‌شود؛ برای Paid یک بدهی بازپس‌گیری (SellerDebt) ساخته می‌شود — فقط برای بسته‌هایی که در
/// همین اجرا تغییر وضعیت داده‌اند، پس اجرای دوباره بدهی دوباره نمی‌سازد.</item>
/// <item>کوپن: فقط وقتی سفارش در همین اجرا Cancelled می‌شود و فقط یک بار (CouponQuotaRefunded).</item>
/// <item>پرداخت: پرداخت‌های باز لغو می‌شوند؛ هر پرداخت موفق به RefundPending می‌رود. هیچ پرداختی اینجا Refunded نمی‌شود.</item>
/// </list>
/// <para>
/// همهٔ بررسی‌ها پیش از هر تغییری انجام می‌شود. ذخیره و تراکنش با فراخوان است. به IOrderService و IPaymentService
/// وابسته نیست تا هر دو بتوانند از آن استفاده کنند (بدون چرخهٔ DI).
/// </para>
/// </summary>
public sealed class OrderReversalService : IOrderReversalService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInventoryService _inventoryService;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderReversalService> _logger;

    public OrderReversalService(
        IUnitOfWork unitOfWork,
        IInventoryService inventoryService,
        IAuditService auditService,
        TimeProvider timeProvider,
        ILogger<OrderReversalService> logger)
    {
        _unitOfWork = unitOfWork;
        _inventoryService = inventoryService;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>آنچه بر سر خودِ سفارش می‌آید.</summary>
    private enum OrderMove
    {
        /// <summary>سفارش لغو می‌شود.</summary>
        Cancel,

        /// <summary>سفارش مرجوع می‌شود.</summary>
        Return,

        /// <summary>سفارش قبلاً لغو/مرجوع شده است؛ فقط پرداخت‌ها هم‌راست می‌شوند.</summary>
        AlreadyReversed,

        /// <summary>همهٔ بسته‌ها در راه‌اند؛ سفارش و بسته‌ها دست نمی‌خورند (فقط برگشت وجه ادمین).</summary>
        LeaveInTransit
    }

    public async Task<OrderReversalResult> ReverseAsync(
        Order order,
        OrderReversalKind kind,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var previousStatus = order.Status;
        var groups = order.SellerGroups.OrderBy(g => g.OrderSellerGroupId).ToList();

        var payments = (await _unitOfWork.Repository<Payment>().ListAsync(
                new Spec<Payment>().Where(p => p.OrderId == order.OrderId).AsTracking(),
                cancellationToken))
            .ToList();

        // ---------------- ۱) بررسی‌ها؛ هنوز هیچ‌چیز تغییر نکرده است ----------------

        // FIN-03 / N8: مشتری فقط تا پیش از ارسال اولین بسته می‌تواند لغو کند؛ پس از آن فقط درخواست مرجوعی.
        if (kind == OrderReversalKind.CustomerCancel
            && groups.Any(g => g.Status is SellerGroupStatus.Shipped or SellerGroupStatus.Delivered or SellerGroupStatus.Returned))
        {
            throw new ConflictException(
                "دست‌کم یکی از بسته‌های این سفارش ارسال شده است و سفارش دیگر قابل لغو نیست؛ پس از دریافت کالا می‌توانید درخواست مرجوعی بدهید.",
                "ORDER_PARTLY_SHIPPED");
        }

        var move = ResolveOrderMove(order, kind);
        var allowReturn = kind is OrderReversalKind.AdminReturn or OrderReversalKind.AdminRefund;

        if (move == OrderMove.Cancel && !allowReturn)
        {
            // همان قاعدهٔ OrderSellerGroup.Cancel: بستهٔ تحویل‌شده یا مرجوعی لغو نمی‌شود (مسیر آن مرجوعی/برگشت وجه است).
            var blocking = groups.FirstOrDefault(g => g.Status is SellerGroupStatus.Delivered or SellerGroupStatus.Returned);
            if (blocking is not null)
                throw new BusinessRuleViolationException($"A {blocking.Status} group cannot be cancelled.", "GROUP_INVALID_STATE");
        }

        // Batch 5 (F-05/F-07): Verify یک پرداخت آغاز شده و نتیجه‌اش نامعلوم است؛ شاید پول برداشته شده باشد.
        // لغو بی‌صدای آن پول را بی‌ردپا می‌کند ⇒ 409.
        if (payments.Any(PaymentLifecycle.IsVerificationUnresolved))
            throw new ConflictException(
                "پرداخت این سفارش در حال تأیید است و هنوز نتیجهٔ قطعی ندارد؛ ابتدا تأیید پرداخت را تکرار کنید.",
                "PAYMENT_VERIFICATION_PENDING");

        // Batch 4c: سفارشی که پولش گرفته شده هرگز با مهلت زمانی لغو نمی‌شود (جاب پیش‌تر خودش آن را کنار می‌گذارد).
        if (kind == OrderReversalKind.AbandonedOrder && payments.Any(p => p.State == PaymentState.Succeeded))
            throw new ConflictException("این سفارش پرداخت شده است و با مهلت زمانی لغو نمی‌شود.", "ORDER_ALREADY_PAID");

        // ---------------- ۲) سفارش و بسته‌ها ----------------

        var changedGroups = new List<OrderSellerGroup>();
        var inTransitGroupIds = new List<int>();

        if (move == OrderMove.Cancel)
            order.Cancel();
        else if (move == OrderMove.Return)
            order.MarkReturned();

        if (move is OrderMove.Cancel or OrderMove.Return)
        {
            foreach (var group in groups)
            {
                switch (group.Status)
                {
                    case SellerGroupStatus.Pending:
                    case SellerGroupStatus.Processing:
                        group.Cancel();
                        await ReleaseStockAsync(order, group, cancellationToken);
                        changedGroups.Add(group);
                        break;

                    case SellerGroupStatus.Shipped:
                        // کالا در راه است، نه در انبار: موجودی آزاد نمی‌شود (FIN-03). اگر بسته برگشت، فروشنده موجودی را دستی اضافه می‌کند.
                        group.Cancel();
                        inTransitGroupIds.Add(group.OrderSellerGroupId);
                        changedGroups.Add(group);
                        break;

                    case SellerGroupStatus.Delivered:
                        group.MarkReturned();
                        await ReleaseStockAsync(order, group, cancellationToken);
                        changedGroups.Add(group);
                        break;

                    // Cancelled / Returned: پایانی؛ کاری نیست.
                }
            }

            // آیتم بدون گروه (دادهٔ پیش از فاز ۳) هرگز ارسال نشده است.
            foreach (var item in order.OrderItems.Where(i => i.OrderSellerGroupId is null && i.OrderSellerGroup is null))
                await _inventoryService.ReleaseAsync(item.ListingId, item.Quantity, cancellationToken);
        }

        // ---------------- ۳) تسویه‌ها و بدهی‌های بازپس‌گیری ----------------

        // سفارشِ در راه دست نمی‌خورد، اما پولش برمی‌گردد: فروشنده نباید بابت آن تسویه بگیرد.
        var payoutGroups = move == OrderMove.LeaveInTransit ? groups : changedGroups;
        var (cancelledPayouts, clawbackDebts) = await ReversePayoutsAsync(order, payoutGroups, move, now, cancellationToken);

        // ---------------- ۴) سهمیهٔ کوپن ----------------

        if (move == OrderMove.Cancel)
            await RestoreCouponQuotaAsync(order, cancellationToken);

        // ---------------- ۵) پرداخت‌ها ----------------

        var cancelledOpenPayments = 0;
        if (move != OrderMove.LeaveInTransit)
        {
            cancelledOpenPayments = await PaymentLifecycle.CancelOpenPaymentsAsync(
                _unitOfWork, _auditService, payments, OpenPaymentReason(kind), cancellationToken);
        }

        var refundRequested = new List<int>();
        foreach (var payment in payments.Where(p => p.State == PaymentState.Succeeded).ToList())
        {
            await PaymentLifecycle.RequestRefundAsync(
                _unitOfWork, _auditService, payment, now, reason, $"order reversal ({kind})", cancellationToken);
            refundRequested.Add(payment.PaymentId);
        }

        // ---------------- ۶) حسابرسی ----------------

        if (move is OrderMove.Cancel or OrderMove.Return)
        {
            await _auditService.RecordAsync(
                move == OrderMove.Return ? "Order.Returned" : "Order.Cancelled",
                nameof(Order),
                order.OrderId.ToString(),
                $"amount={order.TotalAmount} | kind={kind} | from={previousStatus} | reason={reason} " +
                $"| refundRequested={(refundRequested.Count == 0 ? "none" : string.Join(",", refundRequested))} " +
                $"| cancelledPayments={cancelledOpenPayments} | cancelledPayouts={cancelledPayouts} | clawbackDebts={clawbackDebts} " +
                $"| stockNotReleasedForShippedGroups={(inTransitGroupIds.Count == 0 ? "none" : string.Join(",", inTransitGroupIds))}",
                cancellationToken);
        }

        if (refundRequested.Count > 0)
        {
            _logger.LogWarning(
                "Order {OrderId} reversed via {Kind} ({From} → {To}); payments {PaymentIds} are RefundPending and the money must be returned to the customer.",
                order.OrderId, kind, previousStatus, order.Status, refundRequested);
        }
        else
        {
            _logger.LogInformation(
                "Order {OrderId} reversed via {Kind} ({From} → {To}); no refund is owed.",
                order.OrderId, kind, previousStatus, order.Status);
        }

        return new OrderReversalResult(
            previousStatus,
            order.Status,
            refundRequested,
            cancelledOpenPayments,
            cancelledPayouts,
            clawbackDebts);
    }

    /// <summary>
    /// تصمیم دربارهٔ خودِ سفارش؛ برای وضعیت نامجاز همان خطای قاعدهٔ دامنه (<see cref="Order.Cancel"/> /
    /// <see cref="Order.MarkReturned"/>) داده می‌شود و هیچ‌چیز تغییر نمی‌کند.
    /// </summary>
    private static OrderMove ResolveOrderMove(Order order, OrderReversalKind kind)
    {
        switch (kind)
        {
            case OrderReversalKind.AdminReturn:
                if (order.Status != OrderStatus.Delivered)
                    throw new BusinessRuleViolationException("Only delivered orders can be returned.", "ORDER_INVALID_STATE");
                return OrderMove.Return;

            case OrderReversalKind.AdminRefund:
                return order.Status switch
                {
                    OrderStatus.Delivered => OrderMove.Return,
                    OrderStatus.Pending or OrderStatus.Processing => OrderMove.Cancel,
                    OrderStatus.Shipped => OrderMove.LeaveInTransit,
                    _ => OrderMove.AlreadyReversed
                };

            default:
                if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Returned)
                    throw new BusinessRuleViolationException("Cannot cancel shipped, delivered, or returned orders.", "ORDER_INVALID_STATE");

                // لغو دوباره بی‌اثر است: موجودی و کوپن دوباره برنمی‌گردند.
                return order.Status == OrderStatus.Cancelled ? OrderMove.AlreadyReversed : OrderMove.Cancel;
        }
    }

    private async Task ReleaseStockAsync(Order order, OrderSellerGroup group, CancellationToken cancellationToken)
    {
        foreach (var item in order.OrderItems.Where(i => BelongsTo(i, group)))
            await _inventoryService.ReleaseAsync(item.ListingId, item.Quantity, cancellationToken);
    }

    private static bool BelongsTo(OrderItem item, OrderSellerGroup group)
        => ReferenceEquals(item.OrderSellerGroup, group)
           || (item.OrderSellerGroupId is not null && item.OrderSellerGroupId == group.OrderSellerGroupId);

    /// <summary>
    /// تسویهٔ Pending لغو می‌شود. تسویهٔ Paid قابل لغو نیست: یک بدهی بازپس‌گیری برای فروشنده ثبت می‌شود (M7.12) که
    /// از تسویه‌های بعدی او کم می‌شود. تسویهٔ منفی ساخته نمی‌شود (سازندهٔ SellerPayout مبلغ منفی را رد می‌کند).
    /// </summary>
    private async Task<(int Cancelled, int Debts)> ReversePayoutsAsync(
        Order order,
        IReadOnlyCollection<OrderSellerGroup> groups,
        OrderMove move,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
            return (0, 0);

        var groupIds = groups.Select(g => g.OrderSellerGroupId).ToList();

        var payouts = await _unitOfWork.Repository<SellerPayout>().ListAsync(
            new Spec<SellerPayout>()
                .Where(p => p.OrderSellerGroupId != null && groupIds.Contains(p.OrderSellerGroupId.Value))
                .AsTracking(),
            cancellationToken);

        var what = move switch
        {
            OrderMove.Return => "returned",
            OrderMove.Cancel => "cancelled",
            _ => "refunded"
        };

        var cancelled = 0;
        var newDebts = new List<(SellerDebt Debt, SellerPayout Payout)>();

        foreach (var payout in payouts)
        {
            if (payout.Status == PayoutStatus.Pending)
            {
                payout.Cancel();
                _unitOfWork.Repository<SellerPayout>().Update(payout);
                await _auditService.RecordAsync(
                    "SellerPayout.Cancelled",
                    nameof(SellerPayout),
                    payout.SellerPayoutId.ToString(),
                    $"seller={payout.SellerId} amount={payout.Amount} order={order.OrderId} ({what})",
                    cancellationToken);
                cancelled++;
            }
            else if (payout.Status == PayoutStatus.Paid)
            {
                var debt = new SellerDebt
                {
                    SellerId = payout.SellerId,
                    OriginalAmount = payout.Amount,
                    Reason = $"Clawback for {what} order {order.OrderId} (Payout {payout.SellerPayoutId})",
                    CreatedAtUtc = now
                };
                await _unitOfWork.Repository<SellerDebt>().AddAsync(debt, cancellationToken);
                newDebts.Add((debt, payout));
            }
        }

        if (newDebts.Count > 0)
        {
            // Batch 44 (FIN-13): رکورد حسابرسی بدهی باید شناسهٔ واقعی خودِ بدهی را داشته باشد (پیش‌تر شناسهٔ فروشنده
            // ثبت می‌شد). شناسه فقط پس از ذخیره معلوم است؛ پس همین‌جا یک بار ذخیره می‌شود. هر مسیری که تسویهٔ Paid را
            // برمی‌گرداند (لغو/مرجوعی ادمین، برگشت وجه) درون تراکنش فراخوان است، پس شکست بعدی همه را برمی‌گرداند.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var (debt, payout) in newDebts)
            {
                await _auditService.RecordAsync(
                    "SellerDebt.Created",
                    nameof(SellerDebt),
                    debt.SellerDebtId.ToString(),
                    $"seller={debt.SellerId} order={order.OrderId} amount={debt.OriginalAmount} payoutId={payout.SellerPayoutId}",
                    cancellationToken);
            }
        }

        return (cancelled, newDebts.Count);
    }

    /// <summary>M7.13: سهمیهٔ کوپن سفارش لغوشده برمی‌گردد؛ پرچم روی سفارش از برگشت دوباره جلوگیری می‌کند.</summary>
    private async Task RestoreCouponQuotaAsync(Order order, CancellationToken cancellationToken)
    {
        if (!order.CouponId.HasValue || order.CouponQuotaRefunded)
            return;

        var coupon = await _unitOfWork.Repository<Coupon>().FirstOrDefaultAsync(
            new Spec<Coupon>().Where(c => c.CouponId == order.CouponId.Value).AsTracking(),
            cancellationToken);

        if (coupon is not null && coupon.UsedCount > 0)
        {
            coupon.UsedCount--;
            _unitOfWork.Repository<Coupon>().Update(coupon);
        }

        order.CouponQuotaRefunded = true;
    }

    private static string OpenPaymentReason(OrderReversalKind kind) => kind switch
    {
        OrderReversalKind.AbandonedOrder => "abandoned: order cancelled by timeout",
        OrderReversalKind.AdminReturn => "order returned",
        OrderReversalKind.AdminRefund => "order refunded",
        _ => "order cancelled"
    };
}
