using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 6 — مقدار یک ویژگی برای یک واریانت مشخص (مثلاً رنگ=مشکی برای واریانت ۱۲).
    /// </summary>
    [Table("ProductAttributeValue")]
    public class ProductAttributeValue : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ValueId { get; set; }

        [Required]
        public int VariantId { get; set; }

        [ForeignKey(nameof(VariantId))]
        public virtual ProductVariant Variant { get; set; } = null!;

        [Required]
        public int AttributeId { get; set; }

        [ForeignKey(nameof(AttributeId))]
        public virtual ProductAttribute Attribute { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Value { get; set; } = null!;
    }
}