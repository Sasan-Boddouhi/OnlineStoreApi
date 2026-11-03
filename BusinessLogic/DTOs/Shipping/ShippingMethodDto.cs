namespace BusinessLogic.DTOs.Shipping;

public class ShippingMethodDto
{
    public int ShippingMethodId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? CarrierName { get; set; }
    public decimal BaseCost { get; set; }
    public decimal CostPerKg { get; set; }
    public int EstimatedDays { get; set; }
    public bool IsActive { get; set; }
}

public class CreateShippingMethodDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? CarrierName { get; set; }
    public decimal BaseCost { get; set; }
    public decimal CostPerKg { get; set; }
    public int EstimatedDays { get; set; } = 3;
    public bool IsActive { get; set; } = true;
}

public class UpdateShippingMethodDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? CarrierName { get; set; }
    public decimal BaseCost { get; set; }
    public decimal CostPerKg { get; set; }
    public int EstimatedDays { get; set; }
    public bool IsActive { get; set; }
}
