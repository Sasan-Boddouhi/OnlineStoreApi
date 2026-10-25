namespace Application.Options;

/// <summary>Phase 8 — تنظیمات درگاه پرداخت.</summary>
public class PaymentGatewaySettings
{
    public const string SectionName = "PaymentGatewaySettings";

    /// <summary>Manual | Zarinpal | IDPay</summary>
    public string Provider { get; set; } = "Manual";

    /// <summary>کلید درگاه (MerchantId). در production باید از محیط خوانده شود، نه فایل.</summary>
    public string? MerchantId { get; set; }

    /// <summary>آدرس بازگشت کاربر پس از پرداخت.</summary>
    public string CallbackUrl { get; set; } = "https://localhost:5173/payment/callback";

    /// <summary>راز امضای webhook. اگر خالی باشد، webhook غیرفعال است.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>آدرس پایهٔ API درگاه.</summary>
    public string BaseUrl { get; set; } = "https://api.zarinpal.com";

    /// <summary>
    /// Staging: استفاده از سندباکس زرین‌پال.
    /// وقتی true باشد، <see cref="BaseUrl"/> نادیده گرفته می‌شود — تا کسی
    /// تصادفاً با MerchantId سندباکس به درگاه واقعی وصل نشود.
    /// </summary>
    public bool UseSandbox { get; set; }

    /// <summary>آدرس پایهٔ سندباکس زرین‌پال.</summary>
    public const string ZarinpalSandboxBaseUrl = "https://sandbox.zarinpal.com";

    /// <summary>
    /// آدرسی که درگاه واقعاً باید صدا بزند.
    /// سندباکس بر <see cref="BaseUrl"/> اولویت دارد (fail-safe).
    /// </summary>
    public string EffectiveBaseUrl =>
        UseSandbox ? ZarinpalSandboxBaseUrl : BaseUrl;

    /// <summary>اگر true باشد، درگاه واقعی اجازهٔ فراخوانی دارد.</summary>
    public bool EnableRealGateway { get; set; }
}