using Application.Entities;

namespace BusinessLogic.DTOs.Payment;

public class UpdatePaymentDto
{
    public int PaymentId { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? PaymentDate { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? TransactionId { get; set; }
    public PaymentStatus? Status { get; set; }
    public string? Notes { get; set; }
    public bool? IsActive { get; set; }
    public int? InvoiceId { get; set; }
}
