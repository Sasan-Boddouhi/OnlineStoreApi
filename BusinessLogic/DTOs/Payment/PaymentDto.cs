using Application.Entities;

namespace BusinessLogic.DTOs.Payment
{
    public class PaymentDto
    {
        public int PaymentId { get; set; }
        public int InvoiceId { get; set; }
        public int? OrderId { get; set; }
        public decimal Amount { get; set; }
        public string? TransactionId { get; set; }
        public DateTime PaymentDate { get; set; }

        // ---- Phase 8 ----
        public PaymentState State { get; set; }
        public string StateName => State.ToString();
        public PaymentGatewayType Gateway { get; set; }
        public DateTime LastStateChangedAtUtc { get; set; }

        // ---- Batch 42 (FIN-12): برگشت وجه ----

        /// <summary>لحظهٔ ثبت درخواست برگشت وجه (ورود به RefundPending).</summary>
        public DateTime? RefundRequestedAtUtc { get; set; }

        /// <summary>تاریخ برگشت واقعی پول (پس از تأیید ادمین).</summary>
        public DateTime? RefundedAtUtc { get; set; }

        /// <summary>شمارهٔ پیگیری بانکی برگشت وجه.</summary>
        public string? RefundReference { get; set; }

        /// <summary>دلیل برگشت وجه.</summary>
        public string? RefundReason { get; set; }
    }
}