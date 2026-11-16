namespace BusinessLogic.DTOs.Payout;

public class MarkPayoutPaidRequest
{
    public string? Reference { get; set; }

    /// <summary>
    /// Batch 44 (اختیاری): مبلغ خالصی که ادمین واقعاً واریز کرده است (همان <c>netAmount</c> فهرست). اگر با مبلغ خالص
    /// محاسبه‌شده هنگام ثبت برابر نباشد، پاسخ 409 PAYOUT_NET_CHANGED است و پرداخت ثبت نمی‌شود.
    /// </summary>
    public decimal? ExpectedNetAmount { get; set; }
}
