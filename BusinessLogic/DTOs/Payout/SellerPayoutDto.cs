namespace BusinessLogic.DTOs.Payout;

public class SellerPayoutDto
{
    public int SellerPayoutId { get; set; }
    public int SellerId { get; set; }
    public int? OrderSellerGroupId { get; set; }

    /// <summary>مبلغ ناخالص (سهم فروشنده از بسته).</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Batch 44 (F-09): بخشی از مبلغ که بابت بدهی‌های فروشنده کسر می‌شود. برای Paid مقدار ثبت‌شده است؛ برای Pending
    /// پیش‌نمایش با بدهی‌های بازِ فعلی (به ترتیب AvailableAtUtc تسویه‌های Pending همان فروشنده تقسیم می‌شود).
    /// </summary>
    public decimal DebtOffsetAmount { get; set; }

    /// <summary>Batch 44 (F-09): مبلغی که باید (یا شد) به فروشنده واریز شود = Amount − DebtOffsetAmount.</summary>
    public decimal NetAmount { get; set; }

    public string Status { get; set; } = null!;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    /// <summary>Batch 44 (FIN-02): از این لحظه (UTC) قابل پرداخت است؛ پیش از آن pay → 409 PAYOUT_ON_HOLD.</summary>
    public DateTime AvailableAtUtc { get; set; }

    public string? Reference { get; set; }
    public DateTime? PaidAt { get; set; }
}
