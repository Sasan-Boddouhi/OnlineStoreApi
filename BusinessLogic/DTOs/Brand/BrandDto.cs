namespace BusinessLogic.DTOs.Brand;

public class BrandDto
{
    public int BrandId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
}

public class CreateBrandDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateBrandDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
}
