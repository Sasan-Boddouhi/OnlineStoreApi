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
    }
}