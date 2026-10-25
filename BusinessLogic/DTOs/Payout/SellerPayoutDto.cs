namespace BusinessLogic.DTOs.Payout;

public class SellerPayoutDto
{
    public int SellerPayoutId { get; set; }
    public int SellerId { get; set; }
    public int? OrderSellerGroupId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = null!;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string? Reference { get; set; }
    public DateTime? PaidAt { get; set; }
}
