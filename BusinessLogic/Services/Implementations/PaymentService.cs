using System.Text.Json;
using AutoMapper;
using Application.Common.Constants;
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
    private readonly IOrderReversalService _orderReversalService;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentGatewaySettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        IPaymentGateway gateway,
        IPaymentSignatureValidator signatureValidator,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IOrderReversalService orderReversalService,
        TimeProvider timeProvider,
        IOptions<PaymentGatewaySettings> settings,
        ILogger<PaymentService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _gateway = gateway;
        _signatureValidator = signatureValidator;
        _currentUser = currentUser;
        _auditService = auditService;
        _orderReversalService = orderReversalService;
        _timeProvider = timeProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    // Batch 41 (FIN-15): ثبت دستی پرداخت (RecordPaymentAsync، فاز ۰) حذف شد. هیچ فراخوانی در کد محصول نداشت و
    // می‌توانست فاکتور را با هر مبلغی «پرداخت‌شده» کند. پول فقط از مسیر درگاه (Initiate → Verify/Webhook) ثبت می‌شود.

    // ==================== Initiate ====================

    /// <summary>
    /// شروع پرداخت. Batch 5 (F-06):
    ///  • سفارشی که پرداخت موفق دارد → 409 ORDER_ALREADY_PAID؛ سفارش لغوشده/ارسال‌شده/… → 409 ORDER_NOT_PAYABLE.
    ///  • اگر Verify یکی از پرداخت‌های باز سفارش آغاز شده و نتیجه‌اش نامعلوم است → 409 PAYMENT_VERIFICATION_PENDING.
    ///  • پرداخت باز جوان‌تر از <see cref="PaymentGatewaySettings.OpenPaymentReuseMinutes"/> با همان مبلغ دوباره
    ///    برگردانده می‌شود (isReplay=true، همان Authority)؛ پرداخت باز قدیمی‌تر لغو و پرداخت تازه ساخته می‌شود.
    ///  • مبلغ، ریال صحیح است: مبلغ ذخیره‌شده روی Payment دقیقاً همانی است که به درگاه فرستاده و در Verify تکرار می‌شود.
    /// </summary>
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

                return ToReplayResult(existing, orderId);
            }
        }

        var payments = await ListOrderPaymentsAsync(orderId, cancellationToken);
        var refusal = GetPayabilityRefusal(order, payments, payment: null);
        if (refusal is not null)
            throw new ConflictException(refusal.Value.Message, refusal.Value.Code);

        var amount = ToWholeRial(order.TotalAmount, orderId);

        if (amount <= 0)
            throw new BusinessException("مبلغ سفارش نامعتبر است.", "INVALID_PAYMENT_AMOUNT");

        var openPayments = payments.Where(PaymentLifecycle.IsOpen).ToList();
        if (openPayments.Count > 0)
        {
            var unresolved = PaymentLifecycle.FindUnresolvedVerifications(openPayments);
            if (unresolved.Count > 0)
                throw new ConflictException(
                    "تأیید پرداخت قبلی این سفارش هنوز نتیجهٔ قطعی ندارد؛ ابتدا همان پرداخت را دوباره تأیید کنید.",
                    "PAYMENT_VERIFICATION_PENDING");

            var reusable = FindReusableOpenPayment(openPayments, amount);
            if (reusable is not null)
            {
                _logger.LogInformation(
                    "Initiate for order {OrderId} returns open payment {PaymentId} instead of creating a second one",
                    orderId, reusable.PaymentId);
                return ToReplayResult(reusable, orderId);
            }
        }

        // ---- فراخوانی درگاه (ابتدا درگاه صدا زده می‌شود تا در صورت شکست فاکتور بی‌مصرف ساخته نشود) ----
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

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        Payment payment;
        try
        {
            var invoice = await EnsureInvoiceAsync(order, cancellationToken);

            // Batch 5 (F-06): در هر لحظه حداکثر یک پرداخت باز برای سفارش؛ پرداخت باز قدیمی دیگر Verify نمی‌شود.
            await PaymentLifecycle.CancelOpenPaymentsAsync(
                _unitOfWork, _auditService, openPayments, "superseded by a new payment initiation", cancellationToken);

            payment = new Payment
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ثبت شروع پرداخت در AuditLog (بعد از دریافت شناسه از دیتابیس).
            await AuditAsync(
                "Payment.Initiated", payment.PaymentId, $"order={orderId} | amount={amount} IRR", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (key is not null && ex is not DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogWarning(ex, "Concurrent payment insert for key {Key}; returning existing", key);

            var winner = await FindByKeyAsync(key, cancellationToken);

            if (winner is null)
                throw;

            return ToReplayResult(winner, orderId);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

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

    private InitiatePaymentResultDto ToReplayResult(Payment existing, int orderId) => new()
    {
        PaymentId = existing.PaymentId,
        OrderId = existing.OrderId ?? orderId,
        Amount = existing.Amount,
        State = existing.State,
        Authority = existing.TransactionId,
        // Batch 5 (F-28): آدرس صفحهٔ پرداخت فقط از درگاه ساخته می‌شود (همان آدرس شروع اول، سندباکس‌آگاه).
        RedirectUrl = existing.TransactionId is null ? null : _gateway.BuildRedirectUrl(existing.TransactionId),
        IsReplay = true
    };

    private Payment? FindReusableOpenPayment(IEnumerable<Payment> openPayments, decimal amount)
    {
        if (_settings.OpenPaymentReuseMinutes <= 0)
            return null;

        var cutoff = DateTime.UtcNow.AddMinutes(-_settings.OpenPaymentReuseMinutes);

        return openPayments
            .Where(p => p.State == PaymentState.Pending
                        && p.TransactionId is not null
                        && p.Amount == amount
                        && p.LastStateChangedAtUtc >= cutoff)
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefault();
    }

    /// <summary>
    /// Batch 5 (F-27): مبالغ به ریال‌اند و درگاه فقط عدد صحیح می‌گیرد. اگر جمع سفارش کسر ریال داشته باشد
    /// (مثلاً تخفیف درصدی که تا دو رقم اعشار گرد می‌شود — مدل پول در Batch 10)، کسر حذف می‌شود؛ همان رفتار
    /// قبلیِ (long) در درگاه، با این تفاوت که اکنون مبلغ ذخیره‌شدهٔ Payment با مبلغ برداشت‌شده یکی است.
    /// </summary>
    private decimal ToWholeRial(decimal orderTotal, int orderId)
    {
        var whole = decimal.Floor(orderTotal);
        if (whole != orderTotal)
            _logger.LogWarning(
                "Order {OrderId} total {Total} has a fraction of a rial; payment amount is {Whole} IRR",
                orderId, orderTotal, whole);
        return whole;
    }

    // ==================== Verify ====================

    public async Task<PaymentDto> VerifyAsync(string authority, CancellationToken cancellationToken = default)
    {
        var payment = await GetByAuthorityAsync(authority, cancellationToken);

        // M7.10 — بررسی مالکیت کاربر روی سفارش جهت جلوگیری از IDOR
        var role = _currentUser.GetCurrentUserRole();
        var isAdmin = string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
        {
            var userId = _currentUser.TryGetCurrentUserId();
            if (userId is null)
                throw new ForbiddenException("دسترسی به این پرداخت مجاز نیست.", "PAYMENT_FORBIDDEN");

            var customer = await _unitOfWork.Repository<Customer>()
                .FirstOrDefaultAsync(new Spec<Customer>().Where(c => c.UserId == userId.Value), cancellationToken);

            if (customer is null || (payment.Order != null && payment.Order.CustomerId != customer.CustomerId))
                throw new ForbiddenException("این پرداخت متعلق به کاربر دیگری است.", "PAYMENT_FORBIDDEN");
        }

        // تأیید دوبارهٔ پرداخت موفق، خطا نیست؛ همان نتیجه را برمی‌گردانیم.
        if (payment.State == PaymentState.Succeeded)
            return _mapper.Map<PaymentDto>(payment);

        return await VerifyWithGatewayAsync(payment, "verify");
    }

    /// <summary>
    /// Batch 5 — مسیر واحد تأیید برای verify و webhook:
    ///  ۱) F-05/F-06: اگر سفارش دیگر قابل پرداخت نیست (لغو/منقضی/ارسال‌شده) یا پرداخت موفق دیگری دارد، Verify
    ///     صدا زده نمی‌شود؛ پرداخت با دلیل روشن Cancelled و در AuditLog (Payment.NotVerified) ثبت می‌شود و زرین‌پال
    ///     پولِ تأییدنشده را خودکار به خریدار برمی‌گرداند. → 409 ORDER_NOT_PAYABLE / ORDER_ALREADY_PAID
    ///  ۲) F-06 (رقابت): پیش از Verify، پرداخت «ادعا» می‌شود: پرداخت‌های باز دیگر سفارش لغو و خود پرداخت با
    ///     RowVersion به‌روزرسانی می‌شود، در یک تراکنش. از دو Verify هم‌زمان برای دو پرداخت یک سفارش فقط یکی به
    ///     درگاه می‌رسد؛ دیگری تعارض هم‌زمانی می‌گیرد و پس از بارگذاری دوباره رد می‌شود.
    ///  ۳) F-07: Verified/AlreadyVerified ⇒ ثبت قطعی (Batch 4b). Rejected ⇒ Failed. TransientError ⇒ پرداخت
    ///     Pending می‌ماند و 503 PAYMENT_VERIFY_RETRY با Retry-After؛ تکرار بعدی idempotent است (101 ⇒ موفق).
    /// </summary>
    private async Task<PaymentDto> VerifyWithGatewayAsync(Payment payment, string source)
    {
        var ct = CancellationToken.None;
        const int maxClaimAttempts = 2;

        for (var attempt = 1; ; attempt++)
        {
            if (payment.State == PaymentState.Succeeded)
                return _mapper.Map<PaymentDto>(payment);

            var payments = payment.OrderId is null
                ? new List<Payment> { payment }
                : await ListOrderPaymentsAsync(payment.OrderId.Value, ct);
            var order = payment.OrderId is null
                ? null
                : await _unitOfWork.Repository<Order>().FirstOrDefaultAsync(
                    new Spec<Order>().Where(o => o.OrderId == payment.OrderId.Value).AsTracking(), ct);

            var unresolved = PaymentLifecycle.FindUnresolvedVerifications(payments);
            var verifyAlreadyStarted = unresolved.Contains(payment.PaymentId);

            var refusal = order is null ? null : GetPayabilityRefusal(order, payments, payment);
            if (refusal is not null && !verifyAlreadyStarted)
                await RefuseVerificationAsync(payment, refusal.Value.Code, refusal.Value.Message, source);

            if (payment.State != PaymentState.Pending)
                throw new ConflictException(
                    "این پرداخت در وضعیت قابل تأیید نیست.",
                    "PAYMENT_NOT_VERIFIABLE");

            if (unresolved.Any(id => id != payment.PaymentId))
                throw new ServiceUnavailableException(
                    "پرداخت دیگری از همین سفارش در حال تأیید است؛ کمی بعد دوباره تلاش کنید.",
                    "PAYMENT_VERIFY_RETRY",
                    _settings.VerifyRetryAfterSeconds);

            // اگر Verify این پرداخت قبلاً آغاز شده بود (پاسخ نامعلوم)، شاید پول برداشته شده باشد؛ حتی اگر سفارش
            // دیگر قابل پرداخت نباشد، تکرار Verify تنها راه دانستن است (101 ⇒ ثبت Succeeded + بررسی دستی/برگشت).
            if (await TryClaimForVerificationAsync(payment, payments, source, releaseSiblings: refusal is null, attempt < maxClaimAttempts))
                break;

            payment = await GetByAuthorityAsync(payment.TransactionId!, ct);
        }

        var verification = await _gateway.VerifyAsync(payment.TransactionId!, payment.Amount, ct);

        switch (verification.Outcome)
        {
            case PaymentVerificationOutcome.Verified:
            case PaymentVerificationOutcome.AlreadyVerified:
                // Batch 4b: از اینجا پول برداشته شده است؛ ثبت پرداخت نباید شکست بخورد.
                return await CompleteCapturedPaymentAsync(payment, verification.ReferenceId, source);

            case PaymentVerificationOutcome.Rejected:
                return await MarkVerificationRejectedAsync(payment, verification);

            default:
                await AuditAsync(
                    PaymentLifecycle.VerificationPendingAction,
                    payment.PaymentId,
                    $"source={source} | code={verification.ErrorCode} | {verification.ErrorMessage}",
                    ct);
                await _unitOfWork.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "Payment {PaymentId} verification outcome unknown ({Code}); left Pending for retry",
                    payment.PaymentId, verification.ErrorCode);

                throw new ServiceUnavailableException(
                    "نتیجهٔ تأیید پرداخت هنوز از درگاه دریافت نشده است. پرداخت شما ثبت مانده است؛ چند ثانیه بعد دوباره تلاش کنید.",
                    "PAYMENT_VERIFY_RETRY",
                    _settings.VerifyRetryAfterSeconds);
        }
    }

    /// <summary>
    /// سفارش برای این پرداخت قابل پرداخت است؟ پاسخ null یعنی بله.
    /// «پرداخت‌شده» یعنی پرداخت Succeeded دارد (نه فقط وضعیت Processing): سفارش Processing بدون پرداخت
    /// موفق (تأیید ادمین پیش از Batch 7) هنوز قابل پرداخت است و تضمین Batch 4b برای آن برقرار می‌ماند.
    /// </summary>
    private static (string Code, string Message)? GetPayabilityRefusal(
        Order order, IReadOnlyList<Payment> payments, Payment? payment)
    {
        var paid = payments.FirstOrDefault(p => p.State == PaymentState.Succeeded
                                                && (payment is null || p.PaymentId != payment.PaymentId));
        if (paid is not null)
            return ("ORDER_ALREADY_PAID", "این سفارش قبلاً پرداخت شده است.");

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Processing))
            return ("ORDER_NOT_PAYABLE", "این سفارش در وضعیتی نیست که بتوان برایش پرداخت کرد.");

        return null;
    }

    /// <summary>
    /// F-05/F-06: Verify صدا زده نمی‌شود. پرداخت باز با دلیل Cancelled می‌شود (پول تأییدنشده را زرین‌پال
    /// خودکار برمی‌گرداند) و تلاش در AuditLog ثبت می‌شود؛ سپس 409.
    /// </summary>
    private async Task RefuseVerificationAsync(Payment payment, string code, string message, string source)
    {
        var ct = CancellationToken.None;
        var reason = $"not verified: {code} (order {payment.OrderId}); the gateway returns the unverified payment to the payer";

        if (PaymentLifecycle.IsOpen(payment))
        {
            payment.TransitionTo(PaymentState.Cancelled, reason);
            _unitOfWork.Repository<Payment>().Update(payment);
        }

        await AuditAsync(
            PaymentLifecycle.NotVerifiedAction,
            payment.PaymentId,
            $"source={source} | {reason} | paymentState={payment.State} | amount={payment.Amount}",
            ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Payment {PaymentId} was not verified: {Code}. Gateway Verify was not called.", payment.PaymentId, code);

        throw new ConflictException(message, code);
    }

    /// <summary>
    /// «ادعای» پرداخت پیش از Verify، در یک تراکنش: پرداخت‌های باز دیگر همین سفارش لغو می‌شوند (حداکثر یک پرداخت
    /// به درگاه می‌رسد)، خود پرداخت با کنترل RowVersion به‌روزرسانی می‌شود و «تأیید آغازشده» روی خودِ پرداخت
    /// (<see cref="Payment.VerifyStartedAtUtc"/>، Batch 41/FIN-11) نوشته می‌شود؛ رکورد Payment.VerificationStarted در
    /// AuditLog فقط مدرک است. تعارض هم‌زمانی ⇒ false (یک بار دیگر با دادهٔ تازه) یا در تلاش آخر 503 قابل تکرار.
    /// </summary>
    private async Task<bool> TryClaimForVerificationAsync(
        Payment payment,
        IReadOnlyList<Payment> payments,
        string source,
        bool releaseSiblings,
        bool canRetry)
    {
        var ct = CancellationToken.None;

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            if (releaseSiblings)
            {
                await PaymentLifecycle.CancelOpenPaymentsAsync(
                    _unitOfWork,
                    _auditService,
                    payments.Where(p => p.PaymentId != payment.PaymentId),
                    $"superseded: payment {payment.PaymentId} of the same order is being verified",
                    ct);
            }

            // اولین آغاز نگه داشته می‌شود؛ تکرار Verify همان لحظهٔ نخست را دارد.
            payment.VerifyStartedAtUtc ??= _timeProvider.GetUtcNow().UtcDateTime;
            _unitOfWork.Repository<Payment>().Update(payment);
            await AuditAsync(
                PaymentLifecycle.VerificationStartedAction,
                payment.PaymentId,
                $"source={source} | order={payment.OrderId} | amount={payment.Amount} IRR",
                ct);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            _logger.LogWarning(ex,
                "Concurrent change while claiming payment {PaymentId} for verification via {Source}", payment.PaymentId, source);

            if (canRetry)
                return false;

            throw new ServiceUnavailableException(
                "پرداخت دیگری از همین سفارش هم‌زمان در حال تأیید است؛ کمی بعد دوباره تلاش کنید.",
                "PAYMENT_VERIFY_RETRY",
                _settings.VerifyRetryAfterSeconds);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    /// <summary>F-07: فقط ردِ قطعی درگاه پرداخت را Failed می‌کند.</summary>
    private async Task<PaymentDto> MarkVerificationRejectedAsync(Payment payment, PaymentVerificationResult verification)
    {
        var ct = CancellationToken.None;

        payment.TransitionTo(PaymentState.Failed, verification.ErrorMessage ?? verification.ErrorCode);
        _unitOfWork.Repository<Payment>().Update(payment);
        await AuditAsync("Payment.Failed", payment.PaymentId, verification.ErrorCode, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // درخواست رقیب هم‌زمان همین پرداخت را ثبت کرده است (مثلاً تأیید موفق تکراری)؛ همان را برگردان.
            _logger.LogWarning(ex, "Concurrent update while failing payment {PaymentId}; reloading", payment.PaymentId);
            var reloaded = await GetByAuthorityAsync(payment.TransactionId!, ct);
            if (reloaded.State == PaymentState.Succeeded)
                return _mapper.Map<PaymentDto>(reloaded);
            throw;
        }

        _logger.LogWarning(
            "Payment {PaymentId} failed verification: {Code}", payment.PaymentId, verification.ErrorCode);

        // عمداً موجودی آزاد نمی‌شود: کاربر باید بتواند دوباره تلاش کند.
        // آزادسازی رزرو فقط با لغو صریح سفارش انجام می‌شود.
        throw new BusinessException(
            verification.ErrorMessage ?? "پرداخت تأیید نشد.",
            verification.ErrorCode ?? "PAYMENT_VERIFICATION_FAILED");
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

        // 3) idempotent: webhook تکراری نباید وضعیت را عقب ببرد (پرداختِ در انتظار برگشت وجه هم قبلاً ثبت شده است).
        if (payment.State is PaymentState.Succeeded or PaymentState.RefundPending or PaymentState.Refunded)
        {
            _logger.LogInformation("Duplicate webhook for payment {PaymentId} ignored", payment.PaymentId);
            return _mapper.Map<PaymentDto>(payment);
        }

        if (parsed.IsSuccessful)
        {
            // حتی با webhook، نتیجهٔ درگاه دوباره راستی‌آزمایی می‌شود؛ همان گاردهای verify (Batch 5).
            return await VerifyWithGatewayAsync(payment, "webhook");
        }

        if (payment.State != PaymentState.Pending)
            throw new ConflictException("این پرداخت در وضعیت قابل تأیید نیست.", "PAYMENT_NOT_VERIFIABLE");

        // Batch 5 (F-07): اگر Verify این پرداخت آغاز شده و نتیجه‌اش نامعلوم است، پیام «ناموفق» آن را Failed نمی‌کند.
        if (PaymentLifecycle.IsVerificationUnresolved(payment))
        {
            _logger.LogWarning(
                "Failure webhook for payment {PaymentId} ignored: its verification outcome is still unknown", payment.PaymentId);
            return _mapper.Map<PaymentDto>(payment);
        }

        payment.TransitionTo(PaymentState.Failed, note: parsed.Status);
        await AuditAsync("Payment.Failed", payment.PaymentId, parsed.Status, cancellationToken);

        try
        {
            _unitOfWork.Repository<Payment>().Update(payment);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict on webhook for payment {PaymentId}. Likely already processed by redirect verification.", payment.PaymentId);
            var reloaded = await GetByAuthorityAsync(parsed.Authority, cancellationToken);
            return _mapper.Map<PaymentDto>(reloaded);
        }

        return _mapper.Map<PaymentDto>(payment);
    }

    /// <summary>
    /// پرداخت سفارش: پرداخت موفق اگر هست، وگرنه آخرین پرداخت. (Batch 5: با لغو پرداخت‌های جایگزین‌شده، یک سفارش
    /// چند پرداخت دارد و قدیمی‌ترین دیگر نمایندهٔ وضعیت نیست.)
    /// </summary>
    public async Task<PaymentDto?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        await EnsureOrderBelongsToCurrentUserAsync(order, cancellationToken);

        var payments = await _unitOfWork.Repository<Payment>()
            .ListAsync(new Spec<Payment>().Where(p => p.OrderId == orderId), cancellationToken);

        var payment = payments.FirstOrDefault(p => p.State == PaymentState.Succeeded)
                      ?? payments.OrderByDescending(p => p.PaymentId).FirstOrDefault();

        return payment is null ? null : _mapper.Map<PaymentDto>(payment);
    }

    /// <summary>همهٔ پرداخت‌های سفارش، ردیابی‌شده (برای لغو/ادعا در همان واحد کار).</summary>
    private async Task<List<Payment>> ListOrderPaymentsAsync(int orderId, CancellationToken cancellationToken)
        => (await _unitOfWork.Repository<Payment>().ListAsync(
                new Spec<Payment>().Where(p => p.OrderId == orderId).AsTracking(),
                cancellationToken))
            .ToList();

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
                new Spec<Payment>()
                    .Where(p => p.TransactionId == authority)
                    .Include(p => p.Order)
                    .AsTracking(),
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
        if (string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase))
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

    // ==================== Batch 4b: پس از برداشت پول ====================

    /// <summary>
    /// Batch 4b — درگاه پول را برداشته است؛ از این نقطه هیچ خطایی نباید ثبت پرداخت را برگرداند.
    ///
    ///  1) Payment با مرجع درگاه در تراکنش مستقل Succeeded ذخیره می‌شود.
    ///  2) سفارش، گروه‌ها و فاکتور در تراکنش دوم و به‌صورت idempotent همگام می‌شوند؛
    ///     شکست این گام پرداخت ثبت‌شده را برنمی‌گرداند.
    ///  3) هر ناسازگاری واقعی با سطح Error لاگ و با عنوان Payment.ReviewRequired در AuditLog
    ///     ثبت می‌شود تا دستی بررسی شود؛ پاسخ به مشتری همچنان موفق است.
    ///
    /// عمداً CancellationToken.None: قطع اتصال مشتری (RequestAborted) نباید ثبت پولی را که
    /// گرفته شده نیمه‌کاره بگذارد.
    /// </summary>
    private async Task<PaymentDto> CompleteCapturedPaymentAsync(Payment payment, string? referenceId, string source)
    {
        var issues = new List<string>();

        payment = await PersistCapturedPaymentAsync(payment, referenceId, source, issues);

        if (payment.State == PaymentState.Succeeded)
            await SyncOrderAfterCaptureAsync(payment, issues);

        if (issues.Count > 0)
            await RecordReviewRequiredAsync(payment, referenceId, source, issues);

        _logger.LogInformation(
            "Payment {PaymentId} captured for order {OrderId} via {Source}", payment.PaymentId, payment.OrderId, source);

        return _mapper.Map<PaymentDto>(payment);
    }

    /// <summary>
    /// گام ۱: فقط خود پرداخت ذخیره می‌شود. اگر درخواست رقیب (redirect/webhook) هم‌زمان همین
    /// پرداخت را ثبت کرده باشد، نسخهٔ تازه بارگذاری و همان برگردانده می‌شود.
    /// </summary>
    private async Task<Payment> PersistCapturedPaymentAsync(
        Payment payment, string? referenceId, string source, List<string> issues)
    {
        var ct = CancellationToken.None;
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            // درخواست رقیب پیش‌تر ثبت کرده است ⇒ کار ما تمام است.
            if (payment.State is PaymentState.Succeeded or PaymentState.RefundPending or PaymentState.Refunded)
                return payment;

            if (payment.State != PaymentState.Pending)
            {
                // ماشین حالت پرداخت اجازهٔ Failed/Cancelled → Succeeded نمی‌دهد؛ پول گرفته شده ولی
                // رکورد در وضعیت پایانی دیگری است. فقط بررسی دستی می‌تواند تصمیم بگیرد.
                issues.Add($"gateway captured money but payment was already {payment.State}");
                return payment;
            }

            payment.GatewayReference = referenceId;
            payment.TransitionTo(PaymentState.Succeeded);
            await AuditAsync("Payment.Succeeded", payment.PaymentId, referenceId, ct);
            _unitOfWork.Repository<Payment>().Update(payment);

            await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                await _unitOfWork.SaveChangesAsync(ct);
                await _unitOfWork.CommitTransactionAsync(ct);
                return payment;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                // Rollback، ChangeTracker را هم پاک می‌کند؛ بارگذاری بعدی از دیتابیس است.
                await _unitOfWork.RollbackTransactionAsync(ct);
                _logger.LogWarning(ex,
                    "Concurrent update while recording captured payment {PaymentId} via {Source} (attempt {Attempt}); reloading.",
                    payment.PaymentId, source, attempt);

                payment = await GetByAuthorityAsync(payment.TransactionId!, ct);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                _logger.LogCritical(ex,
                    "Gateway captured money but payment {PaymentId} (authority {Authority}, reference {Reference}) could not be recorded via {Source}.",
                    payment.PaymentId, payment.TransactionId, referenceId, source);
                throw;
            }
        }
    }

    /// <summary>
    /// گام ۲: همگام‌سازی idempotent سفارش با پرداخت موفق.
    ///  • سفارش Pending → Processing؛ سفارش Processing/Shipped/Delivered دست نمی‌خورد.
    ///  • گروه Pending کمیسیون می‌گیرد (Pending → Processing)؛ گروهی که قبلاً کمیسیون گرفته رد می‌شود.
    ///  • فاکتور Paid می‌شود.
    /// هیچ متد دامنه‌ای خارج از گذار مجازش صدا زده نمی‌شود؛ هر وضعیت غیرمنتظره به‌جای
    /// InvalidOperationException به فهرست بررسی دستی اضافه می‌شود.
    /// </summary>
    private async Task SyncOrderAfterCaptureAsync(Payment payment, List<string> issues)
    {
        if (payment.OrderId is null)
            return;

        var ct = CancellationToken.None;

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var order = await _unitOfWork.Repository<Order>()
                .FirstOrDefaultAsync(
                    new Spec<Order>()
                        .Where(o => o.OrderId == payment.OrderId.Value)
                        .Include(o => o.SellerGroups)
                        .AsTracking(),
                    ct);

            if (order is null)
            {
                issues.Add($"order {payment.OrderId} not found");
                await _unitOfWork.RollbackTransactionAsync(ct);
                return;
            }

            // Batch 5 (F-06): درگاه پول دوم را برای سفارشی برداشته که قبلاً پرداخت شده (فقط اگر Verify پیش از گارد آغاز شده بود)؛
            // ثبت Succeeded می‌ماند (Batch 4b) و برگشت آن به بررسی دستی می‌رود (برگشت وجه ادمین، Batch 41: سفارش دست نمی‌خورد).
            var otherSucceeded = await _unitOfWork.Repository<Payment>().AnyAsync(
                new Spec<Payment>().Where(p => p.OrderId == order.OrderId
                                            && p.PaymentId != payment.PaymentId
                                            && p.State == PaymentState.Succeeded),
                ct);
            if (otherSucceeded)
                issues.Add($"order {order.OrderId} already had another succeeded payment; this capture needs a refund");

            ApplyPaidStatus(order, issues);
            _unitOfWork.Repository<Order>().Update(order);

            var invoice = await _unitOfWork.Repository<Invoice>()
                .FirstOrDefaultAsync(
                    new Spec<Invoice>().Where(i => i.OrderId == order.OrderId).AsTracking(),
                    ct);

            if (invoice is not null && invoice.Status != InvoiceStatus.Paid)
            {
                invoice.MarkAsPaid();
                _unitOfWork.Repository<Invoice>().Update(invoice);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch (Exception ex)
        {
            // پرداخت در گام ۱ ثبت شده است؛ شکست این گام فقط بررسی دستی می‌خواهد.
            await _unitOfWork.RollbackTransactionAsync(ct);
            _logger.LogError(ex,
                "Order sync failed after captured payment {PaymentId} for order {OrderId}.",
                payment.PaymentId, payment.OrderId);
            issues.Add($"order sync failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>گذارهای سفارش و گروه‌ها پس از پرداخت؛ هرگز پرتاب نمی‌کند.</summary>
    private static void ApplyPaidStatus(Order order, List<string> issues)
    {
        var wasPending = order.Status == OrderStatus.Pending;

        switch (order.Status)
        {
            case OrderStatus.Pending:
                order.Confirm();
                break;

            case OrderStatus.Processing:
                // مثلاً تأیید ادمین پیش از پرداخت؛ سفارش همین حالا در وضعیت هدف است.
                break;

            case OrderStatus.Shipped:
            case OrderStatus.Delivered:
                // ارسال پیش از ثبت این پرداخت: دادهٔ پیش از Batch 4 یا پرداخت تکراری.
                issues.Add($"order was {order.Status} before this payment was captured");
                return;

            default:
                // Cancelled/Returned: پول گرفته شده برای سفارشی که انجام نمی‌شود ⇒ برگشت دستی.
                issues.Add($"order was {order.Status}; captured money needs a manual refund");
                return;
        }

        foreach (var group in order.SellerGroups.OrderBy(g => g.OrderSellerGroupId))
        {
            switch (group.Status)
            {
                case SellerGroupStatus.Pending:
                    group.ApplyCommission();
                    break;

                case SellerGroupStatus.Processing:
                    // کمیسیون قبلاً (هنگام تأیید) قفل شده است.
                    break;

                case SellerGroupStatus.Shipped:
                case SellerGroupStatus.Delivered:
                    if (wasPending)
                        issues.Add($"group {group.OrderSellerGroupId} was {group.Status} while the order was unpaid");
                    break;

                default:
                    issues.Add($"group {group.OrderSellerGroupId} was {group.Status}; its share of the payment needs a manual refund");
                    break;
            }
        }
    }

    /// <summary>
    /// گام ۳: ناسازگاری‌ها با سطح Error لاگ و در AuditLog (Payment.ReviewRequired) ثبت می‌شوند.
    /// اگر حتی این ذخیره شکست بخورد، لاگ Critical تنها رد باقی‌مانده است و پاسخ همچنان موفق است.
    /// </summary>
    private async Task RecordReviewRequiredAsync(
        Payment payment, string? referenceId, string source, IReadOnlyList<string> issues)
    {
        var details = $"source={source} | order={payment.OrderId?.ToString() ?? "none"} | amount={payment.Amount} " +
                      $"| reference={referenceId} | paymentState={payment.State} | issues: {string.Join("; ", issues)}";

        // Batch 5: برچسب صریح برای گردش‌کار برگشت وجه (Batch 8).
        if (issues.Any(i => i.Contains("refund", StringComparison.OrdinalIgnoreCase)))
            details += " | refundNeeded=true";

        _logger.LogError(
            "Captured payment {PaymentId} needs manual review: {Details}", payment.PaymentId, details);

        try
        {
            await AuditAsync("Payment.ReviewRequired", payment.PaymentId, details, CancellationToken.None);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Could not record manual-review entry for captured payment {PaymentId}: {Details}",
                payment.PaymentId, details);
        }

        // Batch 42 (FIN-12): همان مشکل روی خودِ پرداخت هم علامت می‌خورد تا در صف بررسی (GET payments/review) دیده شود.
        // ذخیرهٔ جدا: اگر این شکست بخورد، رکورد حسابرسیِ بالا از دست نمی‌رود.
        try
        {
            payment.FlagForReview(_timeProvider.GetUtcNow().UtcDateTime, details);
            _unitOfWork.Repository<Payment>().Update(payment);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Could not flag captured payment {PaymentId} for review; only the audit entry exists: {Details}",
                payment.PaymentId, details);
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

    // ==================== Phase 9 (R43) / Batch 41 — برگشت وجه ادمین ====================

    /// <summary>
    /// <b>درخواست</b> برگشت وجه توسط ادمین (Batch 42، FIN-12).
    ///
    /// این متد <b>پول را جابه‌جا نمی‌کند</b>: زرین‌پال برگشت خودکار ندارد و تنها راه واقعی برگشت، پنل بانکی است.
    ///   • پرداخت Succeeded ⇒ روال برگرداندن سفارش (<see cref="IOrderReversalService"/>: سفارش، بسته‌ها، موجودی،
    ///     کوپن، تسویه‌ها و بدهی‌ها) و RefundPending؛ پرداخت در صف برگشت وجه می‌ماند تا پس از واریز در بانک با
    ///     <see cref="ConfirmRefundAsync"/> و شمارهٔ پیگیری تأیید شود. هیچ میان‌بر Succeeded → Refunded وجود ندارد.
    ///   • اگر سفارش پرداخت موفق دیگری هم دارد (برداشت تکراری)، فقط همین پرداخت برمی‌گردد و سفارش دست نمی‌خورد.
    ///   • RefundPending یا هر وضعیت دیگری ⇒ 409 PAYMENT_NOT_REFUNDABLE (کلیک دوباره هرگز چیزی را دوباره ثبت نمی‌کند).
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

            if (payment.State == PaymentState.RefundPending)
                throw new ConflictException(
                    "برگشت وجه این پرداخت قبلاً درخواست شده است؛ پس از واریز در بانک آن را تأیید کنید (refund/confirm).",
                    "PAYMENT_NOT_REFUNDABLE");

            if (payment.State != PaymentState.Succeeded)
                throw new ConflictException(
                    $"فقط پرداخت موفق قابل برگشت است؛ وضعیت فعلی: {payment.State}.",
                    "PAYMENT_NOT_REFUNDABLE");

            // Batch 42: دلیل الزامی است (اعتبارسنج 422)؛ این فقط سد دوم برای فراخوان‌های بدون کنترلر است.
            if (string.IsNullOrWhiteSpace(reason))
                throw new UnprocessableEntityException("دلیل برگشت وجه الزامی است.", "VALIDATION_ERROR");

            var note = reason.Trim();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var reversal = await RequestRefundAndReverseOrderAsync(payment, note, now, cancellationToken);

            await _auditService.RecordAsync(
                AdminRefundRequestedAction,
                nameof(Payment),
                payment.PaymentId.ToString(),
                $"admin refund request | amount={payment.Amount} | order={payment.OrderId?.ToString() ?? "none"} " +
                $"| orderStatus={reversal.OrderStatus} | cancelledPayouts={reversal.CancelledPayouts} " +
                $"| clawbackDebts={reversal.ClawbackDebts} | reason={note} | MONEY_NOT_RETURNED_YET=true",
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogWarning(
                "Refund of payment {PaymentId} requested by admin {AdminId}. Order status: {OrderStatus}, cancelled payouts: {Payouts}, clawback debts: {Debts}. Money must be returned in the bank and then confirmed.",
                payment.PaymentId, _currentUser.TryGetCurrentUserId(), reversal.OrderStatus, reversal.CancelledPayouts, reversal.ClawbackDebts);

            return _mapper.Map<PaymentDto>(payment);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw new ConflictException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>آنچه برگشت وجه بر سر سفارش آورد؛ فقط برای AuditLog و لاگ.</summary>
    private sealed record RefundReversalOutcome(string OrderStatus, int CancelledPayouts, int ClawbackDebts);

    /// <summary>
    /// پرداخت موفق را به RefundPending می‌برد و سفارش مرتبط را از روال مشترک برمی‌گرداند.
    /// سفارشی که پرداخت موفق دیگری دارد برگردانده نمی‌شود: این برگشتِ یک برداشت تکراری است و سفارش هنوز پرداخت‌شده است.
    /// </summary>
    private async Task<RefundReversalOutcome> RequestRefundAndReverseOrderAsync(
        Payment payment,
        string note,
        DateTime now,
        CancellationToken cancellationToken)
    {
        const string source = "admin refund";

        if (payment.OrderId is null)
        {
            await PaymentLifecycle.RequestRefundAsync(_unitOfWork, _auditService, payment, now, note, source, cancellationToken);
            return new RefundReversalOutcome("no-order", 0, 0);
        }

        var order = await _unitOfWork.Repository<Order>()
            .FirstOrDefaultAsync(
                new Spec<Order>()
                    .Where(o => o.OrderId == payment.OrderId.Value)
                    .Include(o => o.OrderItems)
                    .Include(o => o.SellerGroups)
                    .AsTracking(),
                cancellationToken);

        if (order is null)
        {
            await PaymentLifecycle.RequestRefundAsync(_unitOfWork, _auditService, payment, now, note, source, cancellationToken);
            return new RefundReversalOutcome("order-missing", 0, 0);
        }

        var paidByAnotherPayment = await _unitOfWork.Repository<Payment>().AnyAsync(
            new Spec<Payment>().Where(p => p.OrderId == order.OrderId
                                        && p.PaymentId != payment.PaymentId
                                        && p.State == PaymentState.Succeeded),
            cancellationToken);

        if (paidByAnotherPayment)
        {
            _logger.LogWarning(
                "Refund for payment {PaymentId}: order {OrderId} is still paid by another payment; the order is left untouched.",
                payment.PaymentId, order.OrderId);

            await PaymentLifecycle.RequestRefundAsync(_unitOfWork, _auditService, payment, now, note, source, cancellationToken);
            return new RefundReversalOutcome($"{order.Status}-kept (paid by another payment)", 0, 0);
        }

        var result = await _orderReversalService.ReverseAsync(order, OrderReversalKind.AdminRefund, note, cancellationToken);
        _unitOfWork.Repository<Order>().Update(order);

        // روال مشترک هر پرداخت موفق سفارش (از جمله همین پرداخت) را RefundPending کرده است؛ این فقط سد دوم است.
        if (payment.State == PaymentState.Succeeded)
            await PaymentLifecycle.RequestRefundAsync(_unitOfWork, _auditService, payment, now, note, source, cancellationToken);

        if (!result.OrderChanged)
        {
            // Shipped: نه لغو مجاز است نه مرجوعی. عمداً دست نمی‌زنیم و در حسابرسی ثبت می‌کنیم تا کسی تصور نکند فراموش شده.
            _logger.LogWarning(
                "Refund for payment {PaymentId}: order {OrderId} is {Status}; leaving status untouched.",
                payment.PaymentId, order.OrderId, order.Status);
        }

        return new RefundReversalOutcome(
            result.OrderChanged ? order.Status.ToString() : $"{order.Status}-untouched",
            result.CancelledPayouts,
            result.ClawbackDebts);
    }

    // ==================== Batch 42 (FIN-12, CEO-02) — صف برگشت وجه و صف بررسی ====================

    /// <summary>خلاصهٔ حسابرسیِ «ادمین برگشت وجه را درخواست کرد» (جدا از Payment.RefundRequested هر پرداخت).</summary>
    public const string AdminRefundRequestedAction = "Payment.AdminRefundRequested";

    /// <summary>بستن بررسی دستی توسط ادمین.</summary>
    public const string ReviewResolvedAction = "Payment.ReviewResolved";

    private const int MaxQueuePageSize = 100;

    /// <inheritdoc />
    public async Task<PaymentDto> ConfirmRefundAsync(
        int paymentId,
        ConfirmRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // سد دوم پشت اعتبارسنج (422): بدون شمارهٔ پیگیری بانکی، برگشت پول قابل اثبات نیست.
        if (string.IsNullOrWhiteSpace(request.BankReference))
            throw new UnprocessableEntityException("شمارهٔ پیگیری بانکی برگشت وجه الزامی است.", "VALIDATION_ERROR");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var refundedAt = request.RefundedAtUtc?.ToUniversalTime();
        if (refundedAt > now.AddMinutes(5))
            throw new UnprocessableEntityException("تاریخ برگشت وجه نمی‌تواند در آینده باشد.", "VALIDATION_ERROR");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var payment = await _unitOfWork.Repository<Payment>()
                .FirstOrDefaultAsync(
                    new Spec<Payment>().Where(x => x.PaymentId == paymentId).AsTracking(),
                    cancellationToken);

            if (payment is null)
                throw new NotFoundException("پرداخت یافت نشد.", "PAYMENT_NOT_FOUND");

            if (payment.State != PaymentState.RefundPending)
                throw new ConflictException(
                    $"فقط پرداختِ در انتظار برگشت وجه قابل تأیید است؛ وضعیت فعلی: {payment.State}. ابتدا برگشت وجه را درخواست کنید.",
                    "PAYMENT_NOT_REFUND_PENDING");

            var reference = request.BankReference.Trim();
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

            payment.ConfirmRefund(now, reference, note is null ? "refund confirmed" : $"refund confirmed: {note}", refundedAt);
            _unitOfWork.Repository<Payment>().Update(payment);

            await _auditService.RecordAsync(
                PaymentLifecycle.RefundedAction,
                nameof(Payment),
                payment.PaymentId.ToString(),
                $"refund confirmed | amount={payment.Amount} | order={payment.OrderId?.ToString() ?? "none"} " +
                $"| bankReference={payment.RefundReference} | refundedAtUtc={payment.RefundedAtUtc:O} " +
                $"| requestedAtUtc={payment.RefundRequestedAtUtc:O} | note={note ?? "none"} | MONEY_MOVED_MANUALLY=true",
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "Refund of payment {PaymentId} confirmed by admin {AdminId} with bank reference {Reference}.",
                payment.PaymentId, _currentUser.TryGetCurrentUserId(), payment.RefundReference);

            return _mapper.Map<PaymentDto>(payment);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw new ConflictException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<BusinessLogic.DTOs.Shared.PagedResult<PaymentDto>> GetRefundQueueAsync(
        int pageNumber,
        int pageSize,
        bool includeRefunded = false,
        CancellationToken cancellationToken = default)
    {
        (pageNumber, pageSize) = NormalizePaging(pageNumber, pageSize);

        var spec = new Spec<Payment>();
        if (includeRefunded)
        {
            // در انتظار اول (قدیمی‌ترین درخواست اول)، سپس برگشت‌داده‌شده‌ها (تازه‌ترین اول).
            spec.Where(p => p.State == PaymentState.RefundPending || p.State == PaymentState.Refunded)
                .OrderBy(p => p.State == PaymentState.RefundPending ? 0 : 1)
                .ThenBy(p => p.State == PaymentState.RefundPending ? p.RefundRequestedAtUtc : null)
                .ThenByDescending(p => p.RefundedAtUtc)
                .ThenBy(p => p.PaymentId);
        }
        else
        {
            spec.Where(p => p.State == PaymentState.RefundPending)
                .OrderBy(p => p.RefundRequestedAtUtc)
                .ThenBy(p => p.PaymentId);
        }

        var repository = _unitOfWork.Repository<Payment>();
        var total = await repository.CountAsync(spec, cancellationToken);
        var items = await repository.ListAsync(spec.ApplyPaging((pageNumber - 1) * pageSize, pageSize), cancellationToken);

        return new BusinessLogic.DTOs.Shared.PagedResult<PaymentDto>
        {
            Items = items.Select(p => _mapper.Map<PaymentDto>(p)).ToList(),
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    /// <inheritdoc />
    public async Task<BusinessLogic.DTOs.Shared.PagedResult<PaymentReviewDto>> GetReviewQueueAsync(
        int pageNumber,
        int pageSize,
        bool includeResolved = false,
        CancellationToken cancellationToken = default)
    {
        (pageNumber, pageSize) = NormalizePaging(pageNumber, pageSize);

        var spec = new Spec<Payment>();
        if (includeResolved)
        {
            spec.Where(p => p.ReviewRequiredAtUtc != null)
                .OrderBy(p => p.ReviewResolvedAtUtc == null ? 0 : 1)
                .ThenBy(p => p.ReviewRequiredAtUtc)
                .ThenBy(p => p.PaymentId);
        }
        else
        {
            spec.Where(p => p.ReviewRequiredAtUtc != null && p.ReviewResolvedAtUtc == null)
                .OrderBy(p => p.ReviewRequiredAtUtc)
                .ThenBy(p => p.PaymentId);
        }

        var repository = _unitOfWork.Repository<Payment>();
        var total = await repository.CountAsync(spec, cancellationToken);
        var items = await repository.ListAsync(spec.ApplyPaging((pageNumber - 1) * pageSize, pageSize), cancellationToken);

        return new BusinessLogic.DTOs.Shared.PagedResult<PaymentReviewDto>
        {
            Items = items.Select(p => _mapper.Map<PaymentReviewDto>(p)).ToList(),
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    /// <inheritdoc />
    public async Task<PaymentReviewDto> ResolveReviewAsync(
        int paymentId,
        string note,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new UnprocessableEntityException("شرح بررسی الزامی است.", "VALIDATION_ERROR");

        var payment = await _unitOfWork.Repository<Payment>()
            .FirstOrDefaultAsync(
                new Spec<Payment>().Where(x => x.PaymentId == paymentId).AsTracking(),
                cancellationToken);

        if (payment is null)
            throw new NotFoundException("پرداخت یافت نشد.", "PAYMENT_NOT_FOUND");

        if (!payment.IsReviewOpen)
            throw new ConflictException("این پرداخت بررسیِ بازی ندارد.", "PAYMENT_REVIEW_NOT_OPEN");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var adminId = _currentUser.TryGetCurrentUserId();
        payment.ResolveReview(now, adminId, note.Trim());
        _unitOfWork.Repository<Payment>().Update(payment);

        await _auditService.RecordAsync(
            ReviewResolvedAction,
            nameof(Payment),
            payment.PaymentId.ToString(),
            $"order={payment.OrderId?.ToString() ?? "none"} | amount={payment.Amount} | paymentState={payment.State} " +
            $"| flaggedAtUtc={payment.ReviewRequiredAtUtc:O} | note={note.Trim()}",
            cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }

        return _mapper.Map<PaymentReviewDto>(payment);
    }

    private static (int PageNumber, int PageSize) NormalizePaging(int pageNumber, int pageSize)
        => (Math.Max(1, pageNumber), Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, MaxQueuePageSize));
}
