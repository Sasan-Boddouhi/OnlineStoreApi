using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("ProductSubcategory")]
    public class ProductSubcategory : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SubcategoryId { get; set; }
        [Required]
        [MaxLength(100)]
        public required string SubcategoryName { get; set; }
        [MaxLength(250)]
        public string? Description { get; set; }
        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public required int CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        public virtual ProductCategory Category { get; set; } = null!;
        public virtual ICollection<Product> Products { get; set; } = new HashSet<Product>();

    }
}
