namespace BusinessLogic.DTOs.Payment;

/// <summary>بدنهٔ درخواست برگشت وجه.</summary>
public class RefundPaymentRequest
{
    /// <summary>دلیل برگشت — در AuditLog ثبت می‌شود.</summary>
    public string? Reason { get; set; }
}
