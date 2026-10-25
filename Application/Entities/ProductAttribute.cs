using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 6 — تعریف یک ویژگی در سطح محصول (مثلاً «رنگ» یا «حافظه»).
    /// مقدارها در ProductAttributeValue و به‌ازای هر واریانت ذخیره می‌شوند.
    /// </summary>
    [Table("ProductAttribute")]
    public class ProductAttribute : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttributeId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        public AttributeType Type { get; set; } = AttributeType.Text;

        public virtual ICollection<ProductAttributeValue> Values { get; set; } = new HashSet<ProductAttributeValue>();
    }

    public enum AttributeType
    {
        Text = 0,
        Number = 1,
        Color = 2,
        Boolean = 3
    }
}