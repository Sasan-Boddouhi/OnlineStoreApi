using Application.Entities;

namespace BusinessLogic.DTOs.Payment;

/// <summary>
/// Batch 42 (FIN-12, CEO-02) — یک ردیف صف بررسی دستی پرداخت‌ها (فقط ادمین).
/// جدا از <see cref="PaymentDto"/> است، چون PaymentDto به مشتری هم برگردانده می‌شود و شرح بررسی داخلی است.
/// </summary>
public class PaymentReviewDto
{
    public int PaymentId { get; set; }
    public int? OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentState State { get; set; }
    public string StateName => State.ToString();
    public string? TransactionId { get; set; }
    public string? GatewayReference { get; set; }
    public DateTime LastStateChangedAtUtc { get; set; }

    /// <summary>لحظهٔ علامت خوردن برای بررسی.</summary>
    public DateTime ReviewRequiredAtUtc { get; set; }

    /// <summary>شرح مشکل.</summary>
    public string? ReviewNote { get; set; }

    /// <summary>لحظهٔ بسته شدن بررسی (خالی = باز).</summary>
    public DateTime? ReviewResolvedAtUtc { get; set; }

    /// <summary>ادمینی که بررسی را بست.</summary>
    public int? ReviewResolvedByUserId { get; set; }

    // ---- Batch 43 (FIN-07): جزئیات درگاه برای تطبیق با پنل درگاه ----

    /// <summary>کارمزد درگاه برای این تراکنش (همان عدد پاسخ درگاه).</summary>
    public decimal? GatewayFee { get; set; }

    /// <summary>پرداخت‌کنندهٔ کارمزد به گفتهٔ درگاه.</summary>
    public string? GatewayFeeType { get; set; }

    /// <summary>شمارهٔ کارت ماسک‌شدهٔ پرداخت‌کننده.</summary>
    public string? CardPanMasked { get; set; }
}
