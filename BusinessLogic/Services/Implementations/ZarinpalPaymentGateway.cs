using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Interfaces;
using Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 8 — آداپتور درگاه زرین‌پال (API نسخهٔ ۴).
///
/// این کلاس فقط وقتی ساخته می‌شود که Provider = Zarinpal باشد،
/// و فقط وقتی واقعاً فراخوانی می‌کند که EnableRealGateway = true باشد.
/// بدون MerchantId یا با EnableRealGateway = false، صریحاً شکست می‌خورد
/// تا هرگز به‌طور تصادفی در تست‌ها به شبکهٔ واقعی وصل نشویم.
///
/// هیچ اطلاعات کارتی اینجا رد و بدل نمی‌شود؛ فقط مبلغ، Authority و کد تأیید.
///
/// Batch 5 — همهٔ جزئیات زرین‌پال فقط همین‌جاست (مستند: https://www.zarinpal.com/docs/paymentGateway/):
///  • مبلغ: عدد صحیح ریال؛ واحد پول صریحاً <c>currency = "IRR"</c> فرستاده می‌شود (مقدار پیش‌فرض در مستند
///    ذکر نشده است). verify در مستند فیلد currency ندارد و مبلغ آن «به ریال» است.
///  • کد verify: 100 = تأیید شد، 101 = قبلاً تأیید شده؛ هر دو یعنی پول برداشته شده.
///  • ردِ قطعی فقط برای کدهایی که دربارهٔ خودِ پرداخت قطعی‌اند (‎-50 مبلغ نابرابر، ‎-51 پرداخت ناموفق،
///    ‎-53 متعلق به مرچنت دیگر، ‎-54 Authority نامعتبر). خطای شبکه، timeout، 5xx، پاسخ ناخوانا و کدهای
///    پیکربندی/محدودیت (مثلاً ‎-9، ‎-10، ‎-12، ‎-52) «نامعلوم» هستند و پرداخت Failed نمی‌شود.
///  • صفحهٔ پرداخت: <c>{BaseUrl}/pg/StartPay/{authority}</c> از همان یک تنظیم (سندباکس: sandbox.zarinpal.com).
/// </summary>
public sealed class ZarinpalPaymentGateway : IPaymentGateway
{
    /// <summary>واحد پول ارسالی به زرین‌پال (تصمیم مالک D2: مبالغ به ریال).</summary>
    public const string Currency = "IRR";

    public const int CodeVerified = 100;
    public const int CodeAlreadyVerified = 101;

    /// <summary>کدهایی که دربارهٔ خودِ پرداخت قطعی‌اند؛ پولی برداشته نشده است.</summary>
    private static readonly HashSet<int> DefinitiveRejectionCodes = new() { -50, -51, -53, -54 };

    private readonly HttpClient _httpClient;
    private readonly PaymentGatewaySettings _settings;
    private readonly ILogger<ZarinpalPaymentGateway> _logger;

    public ZarinpalPaymentGateway(
        HttpClient httpClient,
        IOptions<PaymentGatewaySettings> settings,
        ILogger<ZarinpalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds > 0 ? _settings.RequestTimeoutSeconds : 20);
    }

    private string BaseUrl => _settings.EffectiveBaseUrl.TrimEnd('/');

    public string BuildRedirectUrl(string authority) => $"{BaseUrl}/pg/StartPay/{authority}";

    /// <summary>
    /// مبلغ ریالی را بدون هیچ گردکردنی به عدد صحیح تبدیل می‌کند؛ مبلغ اعشاری خطای برنامه است و ارسال نمی‌شود
    /// تا مبلغ شروع و تأیید هرگز با هم فرق نکنند.
    /// </summary>
    public static bool TryToRial(decimal amount, out long rial)
    {
        rial = 0;
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > long.MaxValue)
            return false;

        rial = decimal.ToInt64(amount);
        return true;
    }

    public async Task<PaymentInitiationResult> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableRealGateway)
            return PaymentInitiationResult.Fail(
                "GATEWAY_DISABLED", "درگاه واقعی غیرفعال است (EnableRealGateway = false).");

        if (string.IsNullOrWhiteSpace(_settings.MerchantId))
            return PaymentInitiationResult.Fail(
                "GATEWAY_NOT_CONFIGURED", "MerchantId درگاه تنظیم نشده است.");

        if (!TryToRial(request.Amount, out var rial))
            return PaymentInitiationResult.Fail(
                "INVALID_AMOUNT", "مبلغ پرداخت باید عدد صحیح مثبت به ریال باشد.");

        try
        {
            var payload = new
            {
                merchant_id = _settings.MerchantId,
                amount = rial,
                currency = Currency,
                description = request.Description ?? $"Order {request.OrderId}",
                callback_url = request.CallbackUrl,
                metadata = new { order_id = request.OrderId.ToString(CultureInfo.InvariantCulture) }
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"{BaseUrl}/pg/v4/payment/request.json",
                payload,
                cancellationToken);

            var (code, data) = await ReadCodeAsync(response, cancellationToken);

            if (code != CodeVerified || data is null
                || !data.Value.TryGetProperty("authority", out var authorityElement)
                || string.IsNullOrWhiteSpace(authorityElement.GetString()))
            {
                _logger.LogWarning(
                    "Zarinpal request rejected for order {OrderId}: HTTP {Status}, code {Code}",
                    request.OrderId, (int)response.StatusCode, code);
                return PaymentInitiationResult.Fail(
                    "GATEWAY_REJECTED", $"درگاه درخواست پرداخت را نپذیرفت (کد {code?.ToString(CultureInfo.InvariantCulture) ?? "نامعلوم"}).");
            }

            var authority = authorityElement.GetString()!;
            return PaymentInitiationResult.Ok(authority, BuildRedirectUrl(authority));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // خطای شبکه هرگز نباید به «پرداخت موفق» تبدیل شود. هنوز پولی پرداخت نشده است.
            _logger.LogError(ex, "Zarinpal initiate failed for order {OrderId}", request.OrderId);
            return PaymentInitiationResult.Fail("GATEWAY_ERROR", "ارتباط با درگاه برقرار نشد.");
        }
    }

    public async Task<PaymentVerificationResult> VerifyAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        // پیکربندی نادرست ثابت نمی‌کند پرداخت ناموفق است؛ پس از اصلاح، تأیید دوباره ممکن است.
        if (!_settings.EnableRealGateway)
            return PaymentVerificationResult.TransientError(
                "GATEWAY_DISABLED", "درگاه واقعی غیرفعال است (EnableRealGateway = false).");

        if (string.IsNullOrWhiteSpace(_settings.MerchantId))
            return PaymentVerificationResult.TransientError(
                "GATEWAY_NOT_CONFIGURED", "MerchantId درگاه تنظیم نشده است.");

        if (!TryToRial(amount, out var rial))
            return PaymentVerificationResult.TransientError(
                "INVALID_AMOUNT", "مبلغ پرداخت باید عدد صحیح مثبت به ریال باشد.");

        try
        {
            // مستند verify فیلد currency ندارد؛ مبلغ همان عدد صحیح ریالی ارسال‌شده در request است.
            var payload = new
            {
                merchant_id = _settings.MerchantId,
                amount = rial,
                authority
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"{BaseUrl}/pg/v4/payment/verify.json",
                payload,
                cancellationToken);

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning(
                    "Zarinpal verify returned HTTP {Status} for authority {Authority}; outcome unknown",
                    (int)response.StatusCode, authority);
                return PaymentVerificationResult.TransientError(
                    "GATEWAY_UNAVAILABLE", "درگاه موقتاً در دسترس نیست.");
            }

            var (code, data) = await ReadCodeAsync(response, cancellationToken);

            if (code is null)
            {
                _logger.LogWarning(
                    "Zarinpal verify returned HTTP {Status} without a result code for authority {Authority}; outcome unknown",
                    (int)response.StatusCode, authority);
                return PaymentVerificationResult.TransientError(
                    "GATEWAY_BAD_RESPONSE", "پاسخ درگاه قابل خواندن نبود.");
            }

            var referenceId = data is not null && data.Value.TryGetProperty("ref_id", out var refId)
                ? refId.ToString()
                : null;

            if (code == CodeVerified)
                return PaymentVerificationResult.Verified(referenceId);

            if (code == CodeAlreadyVerified)
                return PaymentVerificationResult.AlreadyVerified(referenceId);

            if (DefinitiveRejectionCodes.Contains(code.Value))
                return PaymentVerificationResult.Rejected(
                    "GATEWAY_REJECTED", $"پرداخت توسط درگاه تأیید نشد (کد {code.Value.ToString(CultureInfo.InvariantCulture)}).");

            _logger.LogError(
                "Zarinpal verify returned code {Code} (HTTP {Status}) for authority {Authority}; treated as unknown outcome",
                code, (int)response.StatusCode, authority);
            return PaymentVerificationResult.TransientError(
                "GATEWAY_ERROR", $"درگاه نتیجهٔ قطعی برنگرداند (کد {code.Value.ToString(CultureInfo.InvariantCulture)}).");
        }
        catch (Exception ex)
        {
            // timeout (TaskCanceledException)، خطای شبکه یا JSON ناخوانا: ممکن است درگاه پول را برداشته باشد.
            _logger.LogError(ex, "Zarinpal verify failed for authority {Authority}; outcome unknown", authority);
            return PaymentVerificationResult.TransientError("GATEWAY_ERROR", "ارتباط با درگاه برقرار نشد.");
        }
    }

    public Task<bool> RefundAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        // برگشت وجه خودکار در این مرحله پیاده نشده است (Batch 8)؛ باید دستی از پنل انجام شود.
        _logger.LogWarning("Zarinpal refund requested for {Authority}; manual refund required.", authority);
        return Task.FromResult(false);
    }

    /// <summary>
    /// کد نتیجه را از پاسخ زرین‌پال می‌خواند: موفق در <c>data.code</c>، خطا در <c>errors.code</c>.
    /// اگر هیچ‌کدام نبود، null.
    /// </summary>
    private static async Task<(int? Code, JsonElement? Data)> ReadCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (body.ValueKind != JsonValueKind.Object)
            return (null, null);

        if (body.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("code", out var dataCode)
            && dataCode.TryGetInt32(out var code))
        {
            return (code, data);
        }

        if (body.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Object
            && errors.TryGetProperty("code", out var errorCode)
            && errorCode.TryGetInt32(out var error))
        {
            return (error, null);
        }

        return (null, null);
    }
}
