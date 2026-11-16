using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 9 (R44) — پرداخت‌های Pending که کاربر رهایشان کرده، برای همیشه Pending می‌ماندند.
/// نتیجه: آمار «پرداخت در انتظار» متورم و تسویه‌های نامفهوم.
///
/// Batch 43 (FIN-05): پیش از لغو، از درگاه استعلام می‌شود. خریداری که پول داده ولی مرورگرش پیش از Verify بسته شده،
/// دیگر «پرداخت کردم ولی سفارشم لغو شد» نمی‌بیند: پرداختِ او از همان مسیر Verify برداشت می‌شود. این کلاس فقط
/// پرداخت‌های کهنه را انتخاب می‌کند؛ تصمیم پول برای هر پرداخت در
/// <see cref="IPaymentService.ReconcileStalePaymentAsync"/> است (همان قواعد اقدام ادمین).
///
/// Batch 43b: با استعلام خاموش (<c>PaymentGatewaySettings:EnableInquiry = false</c>) پرداخت کهنه‌ای که Verify آن هرگز
/// آغاز نشده (<c>VerifyStartedAtUtc = null</c>) مثل پیش از Batch 43 لغو می‌شود — زرین‌پال فقط با Verify خودِ ما برداشت
/// می‌کند؛ فقط Verify آغازشده با نتیجهٔ نامعلوم به صف بررسی می‌رود.
///
/// هر پرداخت جدا پردازش و ذخیره می‌شود: خطای یک پرداخت بقیه را متوقف نمی‌کند.
/// سفارش دست‌نخورده می‌ماند تا کاربر دوباره تلاش کند (تصمیم فاز ۸).
/// </summary>
public sealed class AbandonedPaymentSweeper : IAbandonedPaymentSweeper
{
    /// <summary>
    /// پس از این تعداد پاسخ «نامعلومِ» پیاپی، همین اجرا دیگر از درگاه نمی‌پرسد: درگاه در دسترس نیست و هر تماس تا
    /// سقف timeout طول می‌کشد. باقی پرداخت‌ها دست‌نخورده برای اجرای بعد می‌مانند.
    /// </summary>
    public const int MaxConsecutiveUnknownAnswers = 5;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPaymentService _paymentService;
    private readonly TimeProvider _timeProvider;
    private readonly AbandonedPaymentSettings _settings;
    private readonly ILogger<AbandonedPaymentSweeper> _logger;

    public AbandonedPaymentSweeper(
        IUnitOfWork unitOfWork,
        IPaymentService paymentService,
        TimeProvider timeProvider,
        IOptions<AbandonedPaymentSettings> settings,
        ILogger<AbandonedPaymentSweeper> logger)
    {
        _unitOfWork = unitOfWork;
        _paymentService = paymentService;
        _timeProvider = timeProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var timeoutMinutes = Math.Max(1, _settings.TimeoutMinutes);
        var cutoff = now.AddMinutes(-timeoutMinutes);
        var batchSize = Math.Clamp(_settings.MaxPaymentsPerSweep, 1, 5000);

        // پرداخت‌هایی که هنوز برای بررسی علامت نخورده‌اند اول می‌آیند (قدیمی‌ترین اول)؛ پرداخت‌هایی که منتظر پاسخ
        // درگاه مانده‌اند ته صف می‌روند تا چند پرداختِ گیرکرده جلوی بقیه را نگیرند.
        var staleIds = (await _unitOfWork.Repository<Payment>()
                .ListAsync(
                    new Spec<Payment>()
                        .Where(p => p.State == PaymentState.Pending && p.LastStateChangedAtUtc < cutoff)
                        .OrderBy(p => p.ReviewRequiredAtUtc == null ? 0 : 1)
                        .ThenBy(p => p.LastStateChangedAtUtc)
                        .ThenBy(p => p.PaymentId)
                        .ApplyPaging(0, batchSize),
                    cancellationToken))
            .Select(p => p.PaymentId)
            .ToList();

        if (staleIds.Count == 0)
            return 0;

        var cancelled = 0;
        var captured = 0;
        var leftPending = 0;
        var failed = 0;
        var consecutiveUnknown = 0;

        foreach (var paymentId in staleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PaymentReconciliationDto result;
            try
            {
                result = await _paymentService.ReconcileStalePaymentAsync(
                    paymentId, _settings.InquireGatewayBeforeCancel, timeoutMinutes, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // یک پرداخت خراب نباید بقیه را متوقف کند. تراکنش بازمانده برگردانده و ردیابی پاک می‌شود تا تغییر نیمه‌کارهٔ
                // این پرداخت با ذخیرهٔ پرداخت بعدی نوشته نشود.
                failed++;
                _logger.LogError(ex, "Reconciling stale payment {PaymentId} failed; it is retried on the next run.", paymentId);
                await _unitOfWork.BeginTransactionAsync(CancellationToken.None);
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                continue;
            }

            switch (result.Outcome)
            {
                case nameof(PaymentReconciliationOutcome.Cancelled):
                    cancelled++;
                    break;
                case nameof(PaymentReconciliationOutcome.Captured):
                case nameof(PaymentReconciliationOutcome.CapturedNeedsReview):
                    captured++;
                    break;
                case nameof(PaymentReconciliationOutcome.LeftPending):
                    leftPending++;
                    break;
            }

            // استعلامِ خاموش (PaymentGatewaySettings:EnableInquiry=false) تماسی با درگاه نگرفته است؛ نشانهٔ قطعی درگاه نیست
            // و نباید جلوی علامت خوردن بقیهٔ پرداخت‌های کهنه را بگیرد.
            if (result.GatewayErrorCode == PaymentInquiryResult.InquiryDisabledCode)
                continue;

            if (result.GatewayStatus == nameof(PaymentInquiryStatus.Unknown))
            {
                if (++consecutiveUnknown >= MaxConsecutiveUnknownAnswers)
                {
                    _logger.LogError(
                        "The payment gateway gave no definitive inquiry answer {Count} times in a row; the abandoned-payment sweep stops asking until the next run.",
                        consecutiveUnknown);
                    break;
                }
            }
            else if (result.GatewayStatus != "NotAsked")
            {
                consecutiveUnknown = 0;
            }
        }

        _logger.LogWarning(
            "Abandoned-payment sweep (older than {Cutoff:u}): {Cancelled} cancelled, {Captured} captured after gateway inquiry, {LeftPending} left pending, {Failed} failed, {Total} examined",
            cutoff, cancelled, captured, leftPending, failed, staleIds.Count);

        return cancelled;
    }
}
