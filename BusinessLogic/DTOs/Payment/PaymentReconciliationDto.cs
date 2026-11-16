namespace BusinessLogic.DTOs.Payment;

/// <summary>Batch 43 (FIN-05) — بدنهٔ <c>POST payments/verify-by-authority</c> (فقط ادمین).</summary>
public class VerifyByAuthorityRequest
{
    /// <summary>شناسهٔ پرداخت نزد درگاه (Authority)؛ الزامی، حداکثر ۱۰۰ کاراکتر.</summary>
    public string? Authority { get; set; }
}

/// <summary>Batch 43 (FIN-05) — آنچه تطبیق یک پرداخت با درگاه انجام داد.</summary>
public enum PaymentReconciliationOutcome
{
    /// <summary>پرداخت از مسیر Verify برداشت و Succeeded ثبت شد و سفارش پرداخت‌شده است.</summary>
    Captured,

    /// <summary>
    /// پول نزد درگاه برداشت شده بود و پرداخت Succeeded ثبت شد، ولی سفارش دیگر قابل پرداخت نبود (یا مشکل دیگری بود)؛
    /// پرداخت در صف بررسی است و برگشت وجه می‌خواهد.
    /// </summary>
    CapturedNeedsReview,

    /// <summary>پرداخت لغو شد (پرداخت نشده بود، یا پرداخت‌شدهٔ تأییدنشده برای سفارشی که دیگر قابل پرداخت نیست).</summary>
    Cancelled,

    /// <summary>درگاه Verify را قطعی رد کرد؛ پرداخت Failed شد و پولی برداشته نشده است.</summary>
    Failed,

    /// <summary>پرداخت Pending ماند (پاسخ نامعلوم درگاه، تأییدِ بی‌نتیجهٔ قبلی، یا هنوز مهلتش نرسیده).</summary>
    LeftPending,

    /// <summary>پرداخت در وضعیتی نبود که چیزی برای انجام باشد (قبلاً موفق، برگشتی، لغوشده یا ناموفق).</summary>
    NoChange
}

/// <summary>Batch 43 (FIN-05) — نتیجهٔ تطبیق یک پرداخت با درگاه (پاسخ اقدام ادمین و خروجی جاب).</summary>
public class PaymentReconciliationDto
{
    /// <summary>پرداخت پس از تطبیق.</summary>
    public PaymentDto Payment { get; set; } = null!;

    /// <summary>
    /// پاسخ درگاه به استعلام: <c>NotPaid</c>، <c>PaidNotVerified</c>، <c>Verified</c>، <c>Unknown</c>، یا
    /// <c>NotAsked</c> وقتی استعلام لازم نبود.
    /// </summary>
    public string GatewayStatus { get; set; } = "NotAsked";

    /// <summary>متن خام وضعیت نزد درگاه (مثلاً PAID یا IN_BANK)، اگر درگاه داده باشد.</summary>
    public string? GatewayRawStatus { get; set; }

    /// <summary>
    /// کد خطای استعلام وقتی <see cref="GatewayStatus"/> برابر <c>Unknown</c> است (مثلاً <c>INQUIRY_DISABLED</c> وقتی
    /// استعلام با تنظیمات خاموش است، یا <c>GATEWAY_UNAVAILABLE</c>)؛ در غیر این صورت null.
    /// </summary>
    public string? GatewayErrorCode { get; set; }

    /// <summary>نام یکی از مقادیر <see cref="PaymentReconciliationOutcome"/>.</summary>
    public string Outcome { get; set; } = nameof(PaymentReconciliationOutcome.NoChange);

    /// <summary>این پرداخت اکنون بررسی دستیِ باز دارد (صف <c>GET payments/review</c>).</summary>
    public bool ReviewRequired { get; set; }

    /// <summary>توضیح کوتاه برای اپراتور.</summary>
    public string Message { get; set; } = string.Empty;
}
