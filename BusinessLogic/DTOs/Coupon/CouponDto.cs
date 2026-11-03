using System;

namespace BusinessLogic.DTOs.Coupon;

public class CouponDto
{
    public int CouponId { get; set; }
    public string Code { get; set; } = null!;
    public decimal DiscountPercentage { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; }
    public int? UsageLimit { get; set; }
    public int? UsageLimitPerUser { get; set; }
    public int UsedCount { get; set; }
}

public class CreateCouponDto
{
    public string Code { get; set; } = null!;
    public decimal DiscountPercentage { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public int? UsageLimit { get; set; }
    public int? UsageLimitPerUser { get; set; }
}

public class UpdateCouponDto
{
    public string Code { get; set; } = null!;
    public decimal DiscountPercentage { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; }
    public int? UsageLimit { get; set; }
    public int? UsageLimitPerUser { get; set; }
}
