using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("Employee")]
    public class Employee : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]

        public int EmployeeId { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Salary { get; set; }

        [Required]
        public DateTime HireDate { get; set; }

        public DateTime? TerminationDate { get; set; }

        [Required]
        [StringLength(10)]
        public required string EmployeeNumber { get; set; }

        // Required 1:1 relationship with User
        [Required]
        public required int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        // Employee type relationship
        [Required]
        public required int EmployeeTypeId { get; set; }
        [ForeignKey("EmployeeTypeId")]
        public virtual EmployeeType EmployeeType { get; set; } = null!;
    }
}