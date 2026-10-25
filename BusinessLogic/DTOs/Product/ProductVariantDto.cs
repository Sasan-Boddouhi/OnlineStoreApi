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
    }

    public class VariantAttributeDto
    {
        public int AttributeId { get; set; }
        public string Name { get; set; } = null!;
        public string Type { get; set; } = null!;
        public string Value { get; set; } = null!;
    }
}