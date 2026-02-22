using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("Warehouse")]
    public class Warehouse : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int WarehouseId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = null!;

        [Required]
        public int AddressId { get; set; }

        [ForeignKey("AddressId")]
        public virtual Address Address { get; set; } = null!;

        public virtual ICollection<Inventory> Inventories { get; set; } = new HashSet<Inventory>();
    }
}
