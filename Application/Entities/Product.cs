using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("Product")]
    public class Product : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProductId { get; set; }
        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }
        [MaxLength(250)]
        public string? Description { get; set; }
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [Required]
        public UnitOfMeasurement Unit { get; set; }
        [MaxLength(20)]
        public string? Barcode { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime? ExpirationDate { get; set; }

        [Required]
        public required int SubcategoryId { get; set; }

        [ForeignKey("SubcategoryId")]
        public virtual ProductSubcategory Subcategory { get; set; } = null!;

        [Required]
        public bool IsActive { get; set; } = true;

        public virtual ICollection<Inventory> Inventories { get; set; } = new HashSet<Inventory>();
    }
    public enum UnitOfMeasurement
    {
        Piece,          // عدد
        Kilogram,       // کیلوگرم
        gram,           // گرم  
        Liter,          // لیتر
        Meter,          // متر
        Centimeter,     // سانتیمتر
        Millimeter      // میلیمتر
    }
}
