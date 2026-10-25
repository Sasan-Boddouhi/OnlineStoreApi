using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        [Required]
        [StringLength(100)]
        public required string DisplayName { get; set; }

        public bool IsSystem { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(200)]
        public string? Description { get; set; }

        public virtual ICollection<Employee> Employees { get; set; } = new HashSet<Employee>();
    }
}