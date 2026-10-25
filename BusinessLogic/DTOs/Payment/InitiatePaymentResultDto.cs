using Application.Entities;

namespace BusinessLogic.DTOs.Payment
{
    /// <summary>Phase 8 — نتیجهٔ شروع پرداخت؛ آدرس هدایت کاربر.</summary>
    public class InitiatePaymentResultDto
    {
        public int PaymentId { get; set; }
        public int OrderId { get; set; }
        public decimal Amount { get; set; }
        public PaymentState State { get; set; }
        public string? Authority { get; set; }
        public string? RedirectUrl { get; set; }

        /// <summary>true اگر این پاسخ از یک درخواست قبلی (همان idempotency key) بازگردانده شده باشد.</summary>
        public bool IsReplay { get; set; }
    }
}