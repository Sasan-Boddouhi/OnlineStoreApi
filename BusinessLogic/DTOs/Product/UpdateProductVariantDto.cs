using Application.Entities;

namespace BusinessLogic.DTOs.Product
{
    public class UpdateProductVariantDto
    {
        public int VariantId { get; set; }
        public string? Name { get; set; }
        public string? Sku { get; set; }
        public string? Barcode { get; set; }
        public bool IsActive { get; set; }
        
        /// <summary>مقادیر ویژگی‌ها؛ هر AttributeId باید متعلق به همین محصول باشد.</summary>
        public List<VariantAttributeInputDto> Attributes { get; set; } = new();
    }
}