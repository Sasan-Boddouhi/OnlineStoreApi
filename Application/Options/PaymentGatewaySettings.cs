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

    /// <summary>
    /// آدرس پایهٔ درگاه زرین‌پال؛ هم API (pg/v4/payment/*.json) و هم صفحهٔ پرداخت (pg/StartPay/{authority})
    /// از همین یک تنظیم ساخته می‌شوند (Batch 5، F-28). مستند v4 زرین‌پال: https://payment.zarinpal.com
    /// </summary>
    public string BaseUrl { get; set; } = "https://payment.zarinpal.com";

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

    /// <summary>
    /// Batch 5: سقف زمان هر فراخوانی درگاه (ثانیه). timeout در تأیید «نتیجهٔ نامعلوم» است، نه شکست.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Batch 5 (F-07): مقدار هدر Retry-After در پاسخ 503 PAYMENT_VERIFY_RETRY (ثانیه).
    /// </summary>
    public int VerifyRetryAfterSeconds { get; set; } = 5;

    /// <summary>
    /// Batch 5 (F-06): شروع دوبارهٔ پرداخت برای سفارشی که پرداخت باز (Pending) جوان‌تر از این مقدار
    /// دارد، همان پرداخت و همان Authority را برمی‌گرداند (isReplay=true)؛ پرداخت باز قدیمی‌تر لغو و
    /// پرداخت تازه ساخته می‌شود. عمر Authority در مستند زرین‌پال ذکر نشده است؛ مقدار کوتاه امن‌تر است.
    /// 0 یعنی هرگز استفادهٔ مجدد نکن.
    /// </summary>
    public int OpenPaymentReuseMinutes { get; set; } = 15;

    /// <summary>
    /// Batch 43 (FIN-05، تصمیم باز N2): استعلام وضعیت تراکنش از زرین‌پال (<c>pg/v4/payment/inquiry.json</c>) مجاز است.
    /// پیش‌فرض <b>false</b>: متد در مستند رسمی هست، ولی فعال بودنش روی مرچنت ما و سقف‌هایش هنوز از پشتیبانی زرین‌پال
    /// تأیید نشده است. تا وقتی خاموش است هیچ تماس استعلامی گرفته نمی‌شود و جاب هرگز خودکار Verify نمی‌کند. Batch 43b:
    /// پرداخت Pending کهنه‌ای که Verify آن هرگز آغاز نشده لغو می‌شود (زرین‌پال فقط با Verify ما برداشت می‌کند)؛ فقط
    /// Verify آغازشده با نتیجهٔ نامعلوم یک بار برای بررسی دستی علامت می‌خورد (صف <c>GET payments/review</c>) و ادمین با
    /// <c>POST payments/verify-by-authority</c> آن را قطعی می‌کند.
    /// پس از تأیید پشتیبانی، با true کردن این کلید، جاب پرداخت‌های رهاشده پرداخت‌های انجام‌شده را خودش برداشت می‌کند.
    /// </summary>
    public bool EnableInquiry { get; set; }

    /// <summary>
    /// Batch 45 (تصمیم N7، سؤال باز N2e): کمترین مبلغ قابل پرداخت در درگاه (ریال).
    /// تخفیف کوپن طوری سقف می‌خورد که مبلغ قابل پرداخت سفارش از این مقدار کمتر نشود؛ کوپنی که سفارش را صفر کند
    /// سفارشی می‌ساخت که هرگز پرداخت نمی‌شد (FIN-09). مقدار ۱۰٬۰۰۰ تا تأیید زرین‌پال فرض است.
    /// </summary>
    public decimal MinimumAmountRial { get; set; } = 10000m;
}
