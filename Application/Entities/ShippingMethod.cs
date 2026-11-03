using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("ShippingMethod")]
    public class ShippingMethod : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ShippingMethodId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string Slug { get; set; } = null!;

        [MaxLength(100)]
        public string? CarrierName { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BaseCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPerKg { get; set; }

        public int EstimatedDays { get; set; } = 3;

        public bool IsActive { get; set; } = true;
    }
}
