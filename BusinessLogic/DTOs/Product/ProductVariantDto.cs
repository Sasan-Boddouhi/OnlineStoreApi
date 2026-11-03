namespace BusinessLogic.DTOs.Product
{
    public class ProductVariantDto
    {
        public int VariantId { get; set; }
        public int ProductId { get; set; }
        public string? Name { get; set; }
        public string? Sku { get; set; }
        public string? Barcode { get; set; }
        public bool IsActive { get; set; }
        public List<VariantAttributeDto> Attributes { get; set; } = new();
        public List<VariantListingDto> Listings { get; set; } = new();
    }

    public class VariantListingDto
    {
        public int ListingId { get; set; }
        public int SellerId { get; set; }
        public string SellerStoreName { get; set; } = null!;
        public decimal Price { get; set; }
        public decimal AvailableQuantity { get; set; }
    }

    public class VariantAttributeDto
    {
        public int AttributeId { get; set; }
        public string Name { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string Value { get; set; } = null!;
    }
}