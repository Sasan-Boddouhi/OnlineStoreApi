namespace BusinessLogic.DTOs.Invoice;

public class UpdateInvoiceDto
{
    public int InvoiceId { get; set; }
    public int? OrderId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal? SubTotalAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? TotalAmount { get; set; }
}
