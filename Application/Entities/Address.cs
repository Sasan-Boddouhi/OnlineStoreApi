using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("Address")]
    public class Address : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AddressId { get; set; }

        [Required]
        public required int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [Required]
        public required int CityId { get; set; }

        [ForeignKey("CityId")]
        public virtual City City { get; set; } = null!;

        [Required]
        [MaxLength(10)]
        public required string Plaque { get; set; }

        [Required]
        [MaxLength(20)]
        public required string Unit { get; set; }

        [Required, MaxLength(20)]
        public required string PostalCode { get; set; }

        [Required, MaxLength(50)]
        public required string RecipientFirstName { get; set; }

        [Required, MaxLength(50)]
        public required string RecipientLastName { get; set; }

        [MaxLength(250)]
        public string? ExtraDescription { get; set; }

        public bool IsDefault { get; set; } = false;

        public virtual ICollection<Warehouse> Warehouses { get; set; } = new HashSet<Warehouse>();
        public virtual ICollection<Order> Orders { get; set; } = new HashSet<Order>();
        public virtual ICollection<Invoice> Invoices { get; set; } = new HashSet<Invoice>();
    }
}
