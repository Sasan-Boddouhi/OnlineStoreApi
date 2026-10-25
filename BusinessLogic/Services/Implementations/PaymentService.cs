using System.Text.Json;
using AutoMapper;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 8 — سرویس پرداخت.
///
/// سه اصل غیرقابل مذاکره:
/// 1) Idempotency در سطح دیتابیس: UNIQUE(IdempotencyKey) + فیلتر NULL.
///    اگر دو درخواست همزمان برسند، دیتابیس یکی را رد می‌کند و ما همان رکورد موجود
///    را برمی‌گردانیم؛ نه اینکه خطا بدهیم و کاربر دو بار پول بدهد.
/// 2) هیچ‌گاه اطلاعات کارت ذخیره نمی‌شود؛ فقط Authority درگاه.
/// 3) امضای webhook تنها احراز هویت آن است؛ اگر راز تنظیم نشده باشد،
///    همهٔ webhook‌ها رد می‌شوند (fail-closed).
/// </summary>
public sealed class PaymentService : IPaymentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentSignatureValidator _signatureValidator;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly PaymentGatewaySettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        IPaymentGateway gateway,
        IPaymentSignatureValidator signatureValidator,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IOptions<PaymentGatewaySettings> settings,
        ILogger<PaymentService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _gateway = gateway;
        _signatureValidator = signatureValidator;
        _currentUser = currentUser;
        _auditService = auditService;
        _settings = settings.Value;
        _logger = logger;
    }

    // ==================== فاز ۰: ثبت دستی پرداخت ====================

    /// <summary>
    /// ثبت دستی پرداخت روی فاکتور (حفظ رفتار فاز ۰).
    /// از همان مسیر ماشین حالت عبور می‌کند تا پرداخت دستی هم State معتبر داشته باشد.
    /// </summary>
    public async Task<PaymentDto> RecordPaymentAsync(
        int invoiceId,
        decimal amount,
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(invoiceId, cancellationToken);

        if (invoice is null)
            throw new BusinessException("فاکتور یافت نشد.", "INVOICE_NOT_FOUND");

        var payment = new Payment
        {
            InvoiceId = invoiceId,
            OrderId = invoice.OrderId,
            Amount = amount,
            TransactionId = transactionId,
            PaymentDate = DateTime.UtcNow,
            Gateway = PaymentGatewayType.Manual,
            State = PaymentState.Initiated
        };

        // پرداخت دستی دو گام دارد: ثبت در انتظار، سپس تأیید.
        // ماشین حالت عمداً میان‌بر Initiated → Succeeded را نمی‌پذیرد.
        payment.TransitionTo(PaymentState.Pending);
        payment.TransitionTo(PaymentState.Succeeded, "Manual payment record.");
        await AuditAsync("Payment.Recorded", payment.PaymentId, transactionId, cancellationToken);

        invoice.MarkAsPaid();

        await _unitOfWork.Repository<Payment>().AddAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Payment recorded for invoice {InvoiceId}", invoiceId);
        return _mapper.Map<PaymentDto>(payment);
    }

    // ==================== Initiate ====================

    public async Task<InitiatePaymentResultDto> InitiateAsync(
        int orderId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(idempotencyKey);

        // ---- Idempotency + ownership, قبل از هر اثر جانبی ----
        var spec = new Spec<Order>().Where(o => o.OrderId == orderId).AsTracking();
        var order = await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(spec, cancellationToken);

        if (order is null)
            throw new NotFoundException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

        await EnsureOrderBelongsToCurrentUserAsync(order, cancellationToken);

        if (key is not null)
        {
            var existing = await FindByKeyAsync(key, cancellationToken);

            if (existing is not null)
            {
                // همان کلید، همان سفارش ⇒ replay بی‌خطر.
                if (existing.OrderId != orderId)
                    throw new BusinessException(
                        "این IdempotencyKey قبلاً برای سفارش دیگری استفاده شده است.",
                        "IDEMPOTENCY_KEY_REUSED");

                _logger.LogInformation(
                    "Idempotent replay for key {Key}, payment {PaymentId}", key, existing.PaymentId);

                return new InitiatePaymentResultDto
                {
                    PaymentId = existing.PaymentId,
                    OrderId = orderId,
                    Amount = existing.Amount,
                    State = existing.State,
                    Authority = existing.TransactionId,
                    RedirectUrl = existing.TransactionId is null
                        ? null
                        : new Uri(new Uri(_settings.BaseUrl), $"pg/StartPay/{existing.TransactionId}").ToString(),
                    IsReplay = true
                };
            }
        }

        // وضعیت سفارش: پرداخت برای سفارش در انتظار یا در حال پردازش معنا دارد.
        if (order.Status is not (OrderStatus.Pending or OrderStatus.Processing))
            throw new BusinessException(
                "این سفارش در وضعیتی نیست که بتوان برایش پرداخت کرد.",
                "ORDER_NOT_PAYABLE");

        var amount = order.TotalAmount;

        if (amount <= 0)
            throw new BusinessException("مبلغ سفارش نامعتبر است.", "INVALID_PAYMENT_AMOUNT");

        var invoice = await EnsureInvoiceAsync(order, cancellationToken);

        // ---- فراخوانی درگاه ----
        var initiation = await _gateway.InitiateAsync(
            new PaymentInitiationRequest(orderId, amount, _settings.CallbackUrl, $"Order {orderId}"),
            cancellationToken);

        if (!initiation.Success)
        {
            _logger.LogWarning(
                "Gateway initiation failed for order {OrderId}: {Code}", orderId, initiation.ErrorCode);

            // هیچ رکورد Pending ساخته نمی‌شود: پرداختی که هرگز شروع نشده، نباید وجود داشته باشد.
            throw new BusinessException(
                initiation.ErrorMessage ?? "شروع پرداخت ناموفق بود.",
                initiation.ErrorCode ?? "PAYMENT_INITIATION_FAILED");
        }

        var payment = new Payment
        {
            Amount = amount,
            OrderId = orderId,
            InvoiceId = invoice.InvoiceId,
            Gateway = ResolveGatewayType(),
            IdempotencyKey = key,
            PaymentMethod = PaymentMethod.CreditCard,
            State = PaymentState.Initiated
        };

        // پرداخت فقط بعد از دریافت Authority معتبر Pending می‌شود.
        payment.TransactionId = initiation.Authority;
        payment.TransitionTo(PaymentState.Pending);

        await _unitOfWork.Repository<Payment>().AddAsync(payment, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (key is not null)
        {
            // مسابقهٔ همزمانی: درخواست دیگر همان کلید را ثبت کرد.
            // این دقیقاً همان حالتی است که idempotency برای آن وجود دارد.
            _logger.LogWarning(ex, "Concurrent payment insert for key {Key}; returning existing", key);

            var winner = await FindByKeyAsync(key, cancellationToken);

            if (winner is null)
                throw;

            return new InitiatePaymentResultDto
            {
                PaymentId = winner.PaymentId,
                OrderId = winner.OrderId ?? orderId,
                Amount = winner.Amount,
                State = winner.State,
                Authority = winner.TransactionId,
                RedirectUrl = initiation.RedirectUrl,
                IsReplay = true
            };
        }

        // ثبت شروع پرداخت در AuditLog (بعد از دریافت شناسه از دیتابیس).
        await AuditAsync(
            "Payment.Initiated", payment.PaymentId, $"order={orderId}", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new InitiatePaymentResultDto
        {
            PaymentId = payment.PaymentId,
            OrderId = orderId,
            Amount = amount,
            State = payment.State,
            Authority = payment.TransactionId,
            RedirectUrl = initiation.RedirectUrl,
            IsReplay = false
        };
    }

    // ==================== Verify ====================

    public async Task<PaymentDto> VerifyAsync(string authority, CancellationToken cancellationToken = default)
    {
        var payment = await GetByAuthorityAsync(authority, cancellationToken);

        // تأیید دوبارهٔ پرداخت موفق، خطا نیست؛ همان نتیجه را برمی‌گردانیم.
        if (payment.State == PaymentState.Succeeded)
            return _mapper.Map<PaymentDto>(payment);

        if (payment.State != PaymentState.Pending)
            throw new BusinessException(
                "این پرداخت در وضعیت قابل تأیید نیست.",
                "PAYMENT_NOT_VERIFIABLE");

        var verification = await _gateway.VerifyAsync(authority, payment.Amount, cancellationToken);

        if (!verification.Success)
        {
            payment.TransitionTo(
                PaymentState.Failed,
                verification.ErrorMessage ?? verification.ErrorCode);

            _unitOfWork.Repository<Payment>().Update(payment);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Payment {PaymentId} failed verification: {Code}", payment.PaymentId, verification.ErrorCode);

            // عمداً موجودی آزاد نمی‌شود: کاربر باید بتواند دوباره تلاش کند.
            // آزادسازی رزرو فقط با لغو صریح سفارش انجام می‌شود.
            throw new BusinessException(
                verification.ErrorMessage ?? "پرداخت تأیید نشد.",
                verification.ErrorCode ?? "PAYMENT_VERIFICATION_FAILED");
        }

        payment.GatewayReference = verification.ReferenceId;
        payment.TransitionTo(PaymentState.Succeeded);
        await AuditAsync("Payment.Succeeded", payment.PaymentId, verification.ReferenceId, cancellationToken);

        await ApplyPaidStatusToOrderAsync(payment, cancellationToken);

        _unitOfWork.Repository<Payment>().Update(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment {PaymentId} succeeded for order {OrderId}", payment.PaymentId, payment.OrderId);

        return _mapper.Map<PaymentDto>(payment);
    }

    // ==================== Webhook ====================

    public async Task<PaymentDto> HandleWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        // 1) امضا اول از همه — قبل از هر تفسیری از payload.
        if (!_signatureValidator.Verify(payload, signature))
        {
            _logger.LogWarning("Rejected webhook with invalid signature.");
            throw new ForbiddenException("امضای webhook نامعتبر است.", "INVALID_SIGNATURE");
        }

        // 2) تفسیر payload
        WebhookPayload parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<WebhookPayload>(payload, JsonOptions)
                ?? throw new JsonException("Empty payload");
        }
        catch (JsonException)
        {
            throw new BusinessException("بدنهٔ webhook نامعتبر است.", "INVALID_WEBHOOK_PAYLOAD");
        }

        if (string.IsNullOrWhiteSpace(parsed.Authority))
            throw new BusinessException("Authority در webhook موجود نیست.", "INVALID_WEBHOOK_PAYLOAD");

        var payment = await GetByAuthorityAsync(parsed.Authority, cancellationToken);

        // 3) idempotent: webhook تکراری نباید وضعیت را عقب ببرد.
        if (payment.State is PaymentState.Succeeded or PaymentState.Refunded)
        {
            _logger.LogInformation("Duplicate webhook for payment {PaymentId} ignored", payment.PaymentId);
            return _mapper.Map<PaymentDto>(payment);
        }

        if (payment.State != PaymentState.Pending)
            throw new BusinessException("این پرداخت در وضعیت قابل تأیید نیست.", "PAYMENT_NOT_VERIFIABLE");

        if (parsed.IsSuccessful)
        {
            // حتی با webhook، نتیجهٔ درگاه دوباره راستی‌آزمایی می‌شود.
            var verification = await _gateway.VerifyAsync(
                parsed.Authority, payment.Amount, cancellationToken);

            if (!verification.Success)
            {
                payment.TransitionTo(PaymentState.Failed, note: verification.ErrorCode);
            await AuditAsync("Payment.Failed", payment.PaymentId, verification.ErrorCode, cancellationToken);
                _unitOfWork.Repository<Payment>().Update(payment);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                throw new BusinessException(
                    verification.ErrorMessage ?? "پرداخت تأیید نشد.",
                    verification.ErrorCode ?? "PAYMENT_VERIFICATION_FAILED");
            }

            payment.GatewayReference = verification.ReferenceId;
            payment.TransitionTo(PaymentState.Succeeded);
            await AuditAsync("Payment.Succeeded", payment.PaymentId, verification.ReferenceId, cancellationToken);
            await ApplyPaidStatusToOrderAsync(payment, cancellationToken);
        }
        else
        {
            payment.TransitionTo(PaymentState.Failed, note: parsed.Status);
            await AuditAsync("Payment.Failed", payment.PaymentId, parsed.Status, cancellationToken);
        }

        _unitOfWork.Repository<Payment>().Update(payment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<PaymentDto>(payment);
    }

    public async Task<PaymentDto?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _unitOfWork.Repository<Payment>()
            .FirstOrDefaultAsync(
                new Spec<Payment>()
                    .Where(p => p.OrderId == orderId)
                    .OrderBy(p => p.PaymentId),
                cancellationToken);

        return payment is null ? null : _mapper.Map<PaymentDto>(payment);
    }

    /// <summary>
    /// Phase 9 (R40): ثبت رویداد از مسیر واحد IAuditService انجام می‌شود.
    /// </summary>
    private Task AuditAsync(string action, int paymentId, string? details, CancellationToken ct)
        => _auditService.RecordAsync(action, nameof(Payment), paymentId.ToString(), details, ct);

    // ==================== Helpers ====================

    private async Task<Payment> GetByAuthorityAsync(string authority, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authority))
            throw new BusinessException("شناسهٔ پرداخت ارسال نشده است.", "AUTHORITY_REQUIRED");

        var payment = await _unitOfWork.Repository<Payment>()
            .FirstOrDefaultAsync(
                new Spec<Payment>().Where(p => p.TransactionId == authority).AsTracking(),
                cancellationToken);

        if (payment is null)
            throw new NotFoundException("پرداخت یافت نشد.", "PAYMENT_NOT_FOUND");

        return payment;
    }

    private async Task<Payment?> FindByKeyAsync(string key, CancellationToken cancellationToken)
        => await _unitOfWork.Repository<Payment>()
            .FirstOrDefaultAsync(
                new Spec<Payment>().Where(p => p.IdempotencyKey == key),
                cancellationToken);

    /// <summary>
    /// مالکیت سفارش. ادمین اجازهٔ دیدن دارد؛ بقیه فقط سفارش خودشان.
    /// </summary>
    private async Task EnsureOrderBelongsToCurrentUserAsync(Order order, CancellationToken cancellationToken)
    {
        var role = _currentUser.GetCurrentUserRole();
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            return;

        var userId = _currentUser.TryGetCurrentUserId();
        if (userId is null)
            throw new ForbiddenException("دسترسی به این سفارش مجاز نیست.", "PAYMENT_FORBIDDEN");

        // Order.CustomerId → Customer.UserId
        var customer = await _unitOfWork.Repository<Customer>()
            .FirstOrDefaultAsync(new Spec<Customer>().Where(c => c.CustomerId == order.CustomerId), cancellationToken);

        if (customer is null || customer.UserId != userId.Value)
            throw new ForbiddenException("این سفارش متعلق به کاربر دیگری است.", "PAYMENT_FORBIDDEN");
    }

    /// <summary>
    /// پرداخت موفق:
    ///  • اگر سفارش Pending است، به Processing می‌رود (کمیسیون هم قفل می‌شود).
    ///  • فاکتور به Paid علامت می‌خورد.
    /// </summary>
    private async Task ApplyPaidStatusToOrderAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.OrderId is null)
            return;

        var order = await _unitOfWork.Repository<Order>()
            .FirstOrDefaultAsync(
                new Spec<Order>()
                    .Where(o => o.OrderId == payment.OrderId.Value)
                    .Include(o => o.SellerGroups)
                    .AsTracking(),
                cancellationToken);

        if (order is null)
            return;

        if (order.Status == OrderStatus.Pending)
        {
            order.Confirm();

            foreach (var group in order.SellerGroups)
                group.ApplyCommission();

            _unitOfWork.Repository<Order>().Update(order);
        }

        var invoice = await _unitOfWork.Repository<Invoice>()
            .FirstOrDefaultAsync(
                new Spec<Invoice>().Where(i => i.OrderId == order.OrderId).AsTracking(),
                cancellationToken);

        if (invoice is not null && invoice.Status != InvoiceStatus.Paid)
        {
            invoice.MarkAsPaid();
            _unitOfWork.Repository<Invoice>().Update(invoice);
        }
    }

    private async Task<Invoice> EnsureInvoiceAsync(Order order, CancellationToken cancellationToken)
    {
        var invoice = await _unitOfWork.Repository<Invoice>()
            .FirstOrDefaultAsync(
                new Spec<Invoice>().Where(i => i.OrderId == order.OrderId).AsTracking(),
                cancellationToken);

        if (invoice is not null)
            return invoice;

        // فاکتور به‌صورت تنبل ساخته می‌شود تا پرداخت برای سفارش Pending هم ممکن باشد.
        invoice = new Invoice
        {
            OrderId = order.OrderId,
            InvoiceNumber = $"INV-{DateTime.UtcNow.Ticks}",
            SubTotalAmount = order.TotalAmount,
            TotalAmount = order.TotalAmount
        };

        await _unitOfWork.Repository<Invoice>().AddAsync(invoice, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return invoice;
    }

    private static string? NormalizeKey(string? key)
        => string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    private PaymentGatewayType ResolveGatewayType()
        => Enum.TryParse<PaymentGatewayType>(_settings.Provider, ignoreCase: true, out var parsed)
            ? parsed
            : PaymentGatewayType.Manual;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>بدنهٔ استاندارد webhook (مستقل از درگاه).</summary>
    private sealed class WebhookPayload
    {
        public string? Authority { get; set; }
        public string? Status { get; set; }

        public bool IsSuccessful
            => string.Equals(Status, "succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase);
    }

        // ==================== Phase 9 (R43) — برگشت وجه ادمین ====================

    /// <summary>
    /// ثبت برگشت وجه توسط ادمین.
    ///
    /// این متد <b>پول را جابه‌جا نمی‌کند</b> و این عمدی است: زرین‌پال
    /// <c>RefundPaymentAsync</c> ندارد، پس تنها راه واقعی برگشت، پنل بانکی است.
    /// کاری که اینجا انجام می‌شود هم‌راست کردن سیستم با واقعیت است — وگرنه
    /// گزارش مالی می‌گوید پول نزد ماست در حالی که برگشته.
    ///
    /// چهار اثر جانبی، همه در یک تراکنش:
    ///   1. Payment.Succeeded → Refunded
    ///   2. Order → Returned (تحویل‌شده) یا Cancelled (پیش از تحویل)
    ///   3. SellerPayoutهای Pending آن سفارش لغو می‌شوند
    ///   4. AuditLog
    /// </summary>
    public async Task<PaymentDto> RefundPaymentAsync(
        int paymentId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var payment = await _unitOfWork.Repository<Payment>()
                .FirstOrDefaultAsync(
                    new Spec<Payment>().Where(x => x.PaymentId == paymentId).AsTracking(),
                    cancellationToken);

            if (payment is null)
                throw new NotFoundException("پرداخت یافت نشد.", "PAYMENT_NOT_FOUND");

            if (payment.State != PaymentState.Succeeded)
                throw new BusinessException(
                    $"فقط پرداخت موفق قابل برگشت است؛ وضعیت فعلی: {payment.State}.",
                    "PAYMENT_NOT_REFUNDABLE");

            var note = string.IsNullOrWhiteSpace(reason) ? "admin refund" : reason!.Trim();

            payment.TransitionTo(PaymentState.Refunded, $"admin refund: {note}");
            _unitOfWork.Repository<Payment>().Update(payment);

            var orderStatus = await UpdateOrderAfterRefundAsync(payment, note, cancellationToken);

            var cancelledPayouts = await CancelOpenPayoutsAsync(payment, cancellationToken);

            await _auditService.RecordAsync(
                "Payment.Refunded",
                nameof(Payment),
                payment.PaymentId.ToString(),
                $"admin refund | amount={payment.Amount} | order={payment.OrderId?.ToString() ?? "none"} " +
                $"| orderStatus={orderStatus} | cancelledPayouts={cancelledPayouts} | reason={note} " +
                "| MONEY_MOVED_MANUALLY=true",
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogWarning(
                "Payment {PaymentId} refunded by admin {AdminId}. Order status: {OrderStatus}, cancelled payouts: {Payouts}. Money must be returned manually.",
                payment.PaymentId, _currentUser.TryGetCurrentUserId(), orderStatus, cancelledPayouts);

            return _mapper.Map<PaymentDto>(payment);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>سفارش مرتبط را به وضعیت سازگار با برگشت وجه می‌برد.</summary>
    private async Task<string> UpdateOrderAfterRefundAsync(
        Payment payment,
        string note,
        CancellationToken cancellationToken)
    {
        if (payment.OrderId is null)
            return "no-order";

        var order = await _unitOfWork.Repository<Order>()
            .FirstOrDefaultAsync(
                new Spec<Order>().Where(o => o.OrderId == payment.OrderId.Value).AsTracking(),
                cancellationToken);

        if (order is null)
            return "order-missing";

        var previous = order.Status;

        switch (order.Status)
        {
            case OrderStatus.Delivered:
                order.MarkReturned();
                break;

            case OrderStatus.Pending:
            case OrderStatus.Processing:
                order.Cancel();
                break;

            default:
                // Shipped: نه لغو مجاز است نه مرجوعی. عمداً دست نمی‌زنیم و
                // در حسابرسی ثبت می‌کنیم تا کسی تصور نکند فراموش شده.
                _logger.LogWarning(
                    "Refund for payment {PaymentId}: order {OrderId} is {Status}; leaving status untouched.",
                    payment.PaymentId, order.OrderId, order.Status);

                return $"{previous}-untouched";
        }

        _unitOfWork.Repository<Order>().Update(order);

        _logger.LogInformation(
            "Order {OrderId} moved {From} → {To} after refund ({Note}).",
            order.OrderId, previous, order.Status, note);

        return order.Status.ToString();
    }

    /// <summary>
    /// تسویه‌های بازِ فروشنده را لغو می‌کند.
    ///
    /// چرا SellerPayout منفی ساخته نمی‌شود: سازندهٔ موجودیت مبلغ منفی را رد
    /// می‌کند و آن محدودیت درست است — یک «تسویهٔ منفی» در گزارش فروشنده
    /// معنایی ندارد. لغو تسویهٔ Pending همان اثر مالی را دارد بدون شکستن invariant.
    ///
    /// تسویه‌ای که قبلاً Paid شده قابل برگشت خودکار نیست؛ در لاگ هشدار داده می‌شود.
    /// </summary>
    private async Task<int> CancelOpenPayoutsAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.OrderId is null)
            return 0;

        var groupIds = (await _unitOfWork.Repository<OrderSellerGroup>()
                .ListAsync(
                    new Spec<OrderSellerGroup>().Where(g => g.OrderId == payment.OrderId.Value),
                    cancellationToken))
            .Select(g => g.OrderSellerGroupId)
            .ToList();

        if (groupIds.Count == 0)
            return 0;

        var payouts = await _unitOfWork.Repository<SellerPayout>()
            .ListAsync(
                new Spec<SellerPayout>()
                    .Where(p => p.OrderSellerGroupId != null
                             && groupIds.Contains(p.OrderSellerGroupId.Value)
                             && p.Status == PayoutStatus.Pending)
                    .AsTracking(),
                cancellationToken);

        foreach (var payout in payouts)
        {
            payout.Cancel();
            _unitOfWork.Repository<SellerPayout>().Update(payout);
        }

        return payouts.Count;
    }
}
