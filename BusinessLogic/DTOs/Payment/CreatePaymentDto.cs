using Application.Entities;

namespace BusinessLogic.DTOs.Payment;

public class CreatePaymentDto
{
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CreditCard;
    public string? TransactionId { get; set; }
    public string? Notes { get; set; }
    public int InvoiceId { get; set; }
}
