using System.Net.Http.Json;
using System.Text.Json;
using Application.Interfaces;
using Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 8 — آداپتور درگاه زرین‌پال.
///
/// این کلاس فقط وقتی ساخته می‌شود که Provider = Zarinpal باشد،
/// و فقط وقتی واقعاً فراخوانی می‌کند که EnableRealGateway = true باشد.
/// بدون MerchantId یا با EnableRealGateway = false، صریحاً شکست می‌خورد
/// تا هرگز به‌طور تصادفی در تست‌ها به شبکهٔ واقعی وصل نشویم.
///
/// هیچ اطلاعات کارتی اینجا رد و بدل نمی‌شود؛ فقط مبلغ، Authority و کد تأیید.
/// </summary>
public sealed class ZarinpalPaymentGateway : IPaymentGateway
{
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

        try
        {
            var payload = new
            {
                merchant_id = _settings.MerchantId,
                amount = (long)request.Amount,
                description = request.Description ?? $"Order {request.OrderId}",
                callback_url = request.CallbackUrl
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"{_settings.EffectiveBaseUrl.TrimEnd('/')}/pg/v4/payment/request.json",
                payload,
                cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            var data = body.GetProperty("data");
            var authority = data.GetProperty("authority").GetString();

            if (string.IsNullOrWhiteSpace(authority))
                return PaymentInitiationResult.Fail("GATEWAY_NO_AUTHORITY", "درگاه شناسهٔ پرداخت برنگرداند.");

            var redirect = $"https://www.zarinpal.com/pg/StartPay/{authority}";
            return PaymentInitiationResult.Ok(authority, redirect);
        }
        catch (Exception ex)
        {
            // خطای شبکه هرگز نباید به «پرداخت موفق» تبدیل شود.
            _logger.LogError(ex, "Zarinpal initiate failed for order {OrderId}", request.OrderId);
            return PaymentInitiationResult.Fail("GATEWAY_ERROR", "ارتباط با درگاه برقرار نشد.");
        }
    }

    public async Task<PaymentVerificationResult> VerifyAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableRealGateway)
            return PaymentVerificationResult.Fail(
                "GATEWAY_DISABLED", "درگاه واقعی غیرفعال است (EnableRealGateway = false).");

        if (string.IsNullOrWhiteSpace(_settings.MerchantId))
            return PaymentVerificationResult.Fail(
                "GATEWAY_NOT_CONFIGURED", "MerchantId درگاه تنظیم نشده است.");

        try
        {
            var payload = new
            {
                merchant_id = _settings.MerchantId,
                amount = (long)amount,
                authority
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"{_settings.EffectiveBaseUrl.TrimEnd('/')}/pg/v4/payment/verify.json",
                payload,
                cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            var data = body.GetProperty("data");
            var code = data.GetProperty("code").GetInt32();

            // 100 = تأیید موفق، 101 = قبلاً تأیید شده (idempotent).
            if (code is 100 or 101)
                return PaymentVerificationResult.Ok(data.GetProperty("ref_id").ToString());

            return PaymentVerificationResult.Fail("GATEWAY_REJECTED", $"کد پاسخ درگاه: {code}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zarinpal verify failed for authority {Authority}", authority);
            return PaymentVerificationResult.Fail("GATEWAY_ERROR", "ارتباط با درگاه برقرار نشد.");
        }
    }

    public Task<bool> RefundAsync(
        string authority,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        // زرین‌پال برگشت خودکار ندارد؛ باید دستی از پنل انجام شود.
        _logger.LogWarning("Zarinpal refund requested for {Authority}; manual refund required.", authority);
        return Task.FromResult(false);
    }
}