using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("EmployeeType")]
    public class EmployeeType : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int EmployeeTypeId { get; set; }

        [Required]
        [StringLength(50)]
        public required string TypeName { get; set; }

        [StringLength(200)]
        public string? Description { get; set; }

        // Navigation property
        public virtual ICollection<Employee> Employees { get; set; } = new HashSet<Employee>();

    }
}
