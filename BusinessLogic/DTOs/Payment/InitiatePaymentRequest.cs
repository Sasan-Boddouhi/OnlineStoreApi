namespace BusinessLogic.DTOs.Payment;

/// <summary>بدنهٔ درخواست شروع پرداخت.</summary>
public class InitiatePaymentRequest
{
    public int OrderId { get; set; }
    public string? IdempotencyKey { get; set; }
}
