using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("Province")]
    public class Province : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProvinceId { get; set; }

        [Required, MaxLength(100)]
        public required string ProvinceName { get; set; }

        public virtual ICollection<City> Cities { get; set; } = new HashSet<City>();
    }
}
