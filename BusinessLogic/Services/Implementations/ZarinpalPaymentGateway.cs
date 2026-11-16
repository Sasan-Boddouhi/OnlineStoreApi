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
///
/// Batch 43 (FIN-05, FIN-07) — مستند خوانده‌شده در 2026-11-14:
///  • استعلام فقط با <c>PaymentGatewaySettings:EnableInquiry = true</c> انجام می‌شود (پیش‌فرض خاموش تا پاسخ پشتیبانی
///    زرین‌پال دربارهٔ فعال بودن و سقف‌های این متد روی مرچنت ما، تصمیم باز N2)؛ خاموش ⇒ Unknown با کد
///    <c>INQUIRY_DISABLED</c> و بدون هیچ تماسی.
///  • استعلام: <c>POST {BaseUrl}/pg/v4/payment/inquiry.json</c> با <c>merchant_id</c> و <c>authority</c>
///    (https://www.zarinpal.com/docs/paymentGateway/otherMethods/Inquiry.html). پاسخ موفق <c>data.code = 100</c> و
///    <c>data.status</c> یکی از VERIFIED، PAID، IN_BANK، FAILED، REVERSED است. مستند صریحاً می‌گوید این متد برای
///    تأیید تراکنش نیست؛ ما هم فقط برای تصمیم‌گیری می‌خوانیم و برداشت همچنان با verify است. سقف نرخ یا عمر
///    تراکنش در مستند نیامده است (کد ‎-12 = تلاش بیش از حد).
///  • نگاشت: VERIFIED ⇒ Verified؛ PAID ⇒ PaidNotVerified؛ FAILED / REVERSED / IN_BANK ⇒ NotPaid (پولی نزد ما نیست
///    و چون verify نمی‌شود، پرداخت دیرهنگام را زرین‌پال برمی‌گرداند)؛ خطای ‎-51 و ‎-54 ⇒ NotPaid؛ هر چیز دیگر
///    (شبکه، timeout، 5xx، ‎-9/‎-10/‎-11/‎-12/‎-52/‎-53، وضعیت ناشناخته) ⇒ Unknown.
///  • متد <c>unVerified.json</c> (فهرست حداکثر ۱۰۰ تراکنش آخرِ تأییدنشده) عمداً استفاده نمی‌شود: Authority هر
///    پرداخت در جدول خودمان هست و سقف ۱۰۰ آن را برای تطبیق ناقابل‌اتکا می‌کند.
///  • verify افزون بر <c>ref_id</c>، <c>fee</c>، <c>fee_type</c> و <c>card_pan</c> (ماسک‌شده) برمی‌گرداند؛ <c>card_hash</c>
///    خوانده نمی‌شود.
/// </summary>
public sealed class ZarinpalPaymentGateway : IPaymentGateway
{
    /// <summary>واحد پول ارسالی به زرین‌پال (تصمیم مالک D2: مبالغ به ریال).</summary>
    public const string Currency = "IRR";

    public const int CodeVerified = 100;
    public const int CodeAlreadyVerified = 101;

    /// <summary>کدهایی که دربارهٔ خودِ پرداخت قطعی‌اند؛ پولی برداشته نشده است.</summary>
    private static readonly HashSet<int> DefinitiveRejectionCodes = new() { -50, -51, -53, -54 };

    /// <summary>Batch 43: خطاهای استعلام که یعنی «برای این Authority پولی نزد ما نیست» (پرداخت ناموفق، Authority نامعتبر).</summary>
    private static readonly HashSet<int> InquiryNotPaidCodes = new() { -51, -54 };

    public const string InquiryStatusVerified = "VERIFIED";
    public const string InquiryStatusPaid = "PAID";
    public const string InquiryStatusInBank = "IN_BANK";
    public const string InquiryStatusFailed = "FAILED";
    public const string InquiryStatusReversed = "REVERSED";

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
                return WithGatewayDetails(PaymentVerificationResult.Verified(referenceId), data);

            if (code == CodeAlreadyVerified)
                return WithGatewayDetails(PaymentVerificationResult.AlreadyVerified(referenceId), data);

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

    /// <summary>
    /// Batch 43 (FIN-05): استعلام وضعیت Authority بدون برداشت پول. هرگز پرتاب نمی‌کند؛ هر پاسخ غیرقطعی Unknown است.
    /// </summary>
    public async Task<PaymentInquiryResult> InquireAsync(
        string authority,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableRealGateway)
            return PaymentInquiryResult.Unknown(
                "GATEWAY_DISABLED", "درگاه واقعی غیرفعال است (EnableRealGateway = false).");

        if (string.IsNullOrWhiteSpace(_settings.MerchantId))
            return PaymentInquiryResult.Unknown(
                "GATEWAY_NOT_CONFIGURED", "MerchantId درگاه تنظیم نشده است.");

        // تصمیم باز N2: تا تأیید پشتیبانی زرین‌پال، استعلام فقط با کلید صریح انجام می‌شود (پیش‌فرض خاموش).
        if (!_settings.EnableInquiry)
            return PaymentInquiryResult.Disabled();

        if (string.IsNullOrWhiteSpace(authority))
            return PaymentInquiryResult.Unknown("AUTHORITY_REQUIRED", "شناسهٔ پرداخت ارسال نشده است.");

        try
        {
            var payload = new
            {
                merchant_id = _settings.MerchantId,
                authority
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"{BaseUrl}/pg/v4/payment/inquiry.json",
                payload,
                cancellationToken);

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning(
                    "Zarinpal inquiry returned HTTP {Status} for authority {Authority}; status unknown",
                    (int)response.StatusCode, authority);
                return PaymentInquiryResult.Unknown("GATEWAY_UNAVAILABLE", "درگاه موقتاً در دسترس نیست.");
            }

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (body.ValueKind != JsonValueKind.Object)
                return PaymentInquiryResult.Unknown("GATEWAY_BAD_RESPONSE", "پاسخ درگاه قابل خواندن نبود.");

            if (body.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("status", out var statusElement)
                && statusElement.ValueKind == JsonValueKind.String)
            {
                var status = statusElement.GetString()!.Trim();
                var code = data.TryGetProperty("code", out var dataCode) && dataCode.TryGetInt32(out var parsed)
                    ? parsed
                    : (int?)null;

                // وضعیت فقط همراه با کد موفق (100) معتبر است؛ کد دیگر یعنی خودِ استعلام موفق نبوده است.
                if (code != CodeVerified)
                    return PaymentInquiryResult.Unknown(
                        "GATEWAY_ERROR",
                        $"درگاه وضعیت قطعی برنگرداند (کد {code?.ToString(CultureInfo.InvariantCulture) ?? "نامعلوم"}).",
                        status);

                return status.ToUpperInvariant() switch
                {
                    InquiryStatusVerified => PaymentInquiryResult.Verified(status),
                    InquiryStatusPaid => PaymentInquiryResult.PaidNotVerified(status),
                    InquiryStatusFailed or InquiryStatusReversed or InquiryStatusInBank => PaymentInquiryResult.NotPaid(status),
                    _ => PaymentInquiryResult.Unknown(
                        "GATEWAY_UNKNOWN_STATUS", "درگاه وضعیتی برگرداند که شناخته‌شده نیست.", status)
                };
            }

            var errorCode = body.TryGetProperty("errors", out var errors) ? ReadErrorCode(errors) : null;

            if (errorCode is not null && InquiryNotPaidCodes.Contains(errorCode.Value))
                return PaymentInquiryResult.NotPaid($"ERROR{errorCode.Value.ToString(CultureInfo.InvariantCulture)}");

            _logger.LogWarning(
                "Zarinpal inquiry returned code {Code} (HTTP {Status}) for authority {Authority}; status unknown",
                errorCode, (int)response.StatusCode, authority);
            return errorCode is null
                ? PaymentInquiryResult.Unknown("GATEWAY_BAD_RESPONSE", "پاسخ درگاه قابل خواندن نبود.")
                : PaymentInquiryResult.Unknown(
                    "GATEWAY_ERROR", $"درگاه وضعیت قطعی برنگرداند (کد {errorCode.Value.ToString(CultureInfo.InvariantCulture)}).");
        }
        catch (Exception ex)
        {
            // timeout، خطای شبکه یا JSON ناخوانا: وضعیت نامعلوم است، نه «پرداخت‌نشده».
            _logger.LogError(ex, "Zarinpal inquiry failed for authority {Authority}; status unknown", authority);
            return PaymentInquiryResult.Unknown("GATEWAY_ERROR", "ارتباط با درگاه برقرار نشد.");
        }
    }

    /// <summary>
    /// کد خطا را از <c>errors</c> می‌خواند. دو شکل در مستند هست: <c>{"code": -54, ...}</c> و شکل اعتبارسنجیِ استعلام
    /// <c>{"authority": ["Invalid authority.", "-54"]}</c> (کد منفی به‌صورت رشته داخل آرایهٔ فیلد).
    /// </summary>
    private static int? ReadErrorCode(JsonElement errors)
    {
        if (errors.ValueKind != JsonValueKind.Object)
            return null;

        if (errors.TryGetProperty("code", out var codeElement))
        {
            if (codeElement.ValueKind == JsonValueKind.Number && codeElement.TryGetInt32(out var code))
                return code;

            if (codeElement.ValueKind == JsonValueKind.String
                && int.TryParse(codeElement.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedCode))
                return parsedCode;
        }

        foreach (var property in errors.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) && number < 0)
                    return number;

                if (item.ValueKind == JsonValueKind.String
                    && int.TryParse(item.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var text)
                    && text < 0)
                    return text;
            }
        }

        return null;
    }

    /// <summary>Batch 43 (FIN-07): کارمزد، پرداخت‌کنندهٔ کارمزد و کارت ماسک‌شده را از پاسخ verify برمی‌دارد.</summary>
    private static PaymentVerificationResult WithGatewayDetails(PaymentVerificationResult result, JsonElement? data)
    {
        if (data is null || data.Value.ValueKind != JsonValueKind.Object)
            return result;

        var element = data.Value;

        decimal? fee = null;
        if (element.TryGetProperty("fee", out var feeElement))
        {
            if (feeElement.ValueKind == JsonValueKind.Number && feeElement.TryGetDecimal(out var number))
                fee = number;
            else if (feeElement.ValueKind == JsonValueKind.String
                     && decimal.TryParse(feeElement.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var text))
                fee = text;
        }

        return result with
        {
            Fee = fee is >= 0 ? fee : null,
            FeeType = ReadString(element, "fee_type"),
            CardPanMasked = ReadString(element, "card_pan")
        };
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

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
