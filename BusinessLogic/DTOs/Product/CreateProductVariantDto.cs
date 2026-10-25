using Application.Entities;

namespace BusinessLogic.DTOs.Product
{
    public class CreateProductVariantDto
    {
        public int ProductId { get; set; }
        public string? Name { get; set; }
        public string? Sku { get; set; }
        public string? Barcode { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>مقادیر ویژگی‌ها؛ هر AttributeId باید متعلق به همین محصول باشد.</summary>
        public List<VariantAttributeInputDto> Attributes { get; set; } = new();
    }

    public class VariantAttributeInputDto
    {
        public int AttributeId { get; set; }
        public string Value { get; set; } = null!;
    }

    public class CreateProductAttributeDto
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = null!;
        public AttributeType Type { get; set; } = AttributeType.Text;
    }
}