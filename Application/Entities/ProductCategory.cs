using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("ProductCategory")]
    public class ProductCategory : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CategoryId { get; set; }
        [Required]
        [MaxLength(100)]
        public required string CategoryName { get; set; }
        [MaxLength(250)]
        public string? Description { get; set; }
        [Required]
        public bool IsActive { get; set; } = true;

        public virtual ICollection<ProductSubcategory> Subcategories { get; set; } = new HashSet<ProductSubcategory>();
    }
}
