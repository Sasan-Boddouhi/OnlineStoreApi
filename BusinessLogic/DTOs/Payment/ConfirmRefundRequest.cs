namespace BusinessLogic.DTOs.Payment;

/// <summary>
/// Batch 42 (FIN-12) — تأیید برگشت واقعی پول توسط ادمین (پس از واریز در پنل بانکی).
/// </summary>
public class ConfirmRefundRequest
{
    /// <summary>شمارهٔ پیگیری بانکی برگشت وجه (الزامی، حداکثر ۱۰۰ کاراکتر).</summary>
    public string? BankReference { get; set; }

    /// <summary>تاریخ واقعی برگشت پول (UTC)؛ اگر خالی باشد لحظهٔ ثبت. تاریخ آینده پذیرفته نمی‌شود.</summary>
    public DateTime? RefundedAtUtc { get; set; }

    /// <summary>یادداشت اختیاری (حداکثر ۵۰۰ کاراکتر).</summary>
    public string? Note { get; set; }
}

/// <summary>Batch 42 (FIN-12) — بستن بررسی دستی یک پرداخت توسط ادمین.</summary>
public class ResolvePaymentReviewRequest
{
    /// <summary>شرح آنچه بررسی و انجام شد (الزامی، حداکثر ۵۰۰ کاراکتر).</summary>
    public string? Note { get; set; }
}
