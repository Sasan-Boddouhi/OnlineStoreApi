using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{

    [Table("User")]
    public class User : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UserId { get; set; }

        [Required]
        [StringLength(100)]
        public required string FirstName { get; set; }

        [Required]
        [StringLength(100)]
        public required string LastName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public required string PasswordHash { get; set; }

        [Required]
        public required UserType UserType { get; set; }

        public DateTime DateOfBirth { get; set; }

        public bool IsActive { get; set; } = true;

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        // Navigation properties برای اطلاعات کاربر
        public string? Email { get; set; }
        public virtual ICollection<Address> Addresses { get; set; } = new HashSet<Address>();
        public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();

        [Required]
        [StringLength(11)]
        public string PhoneNumber { get; set; } = string.Empty;

        // Navigation properties برای Customer و Employee
        public virtual Customer? Customer { get; set; }
        public virtual Employee? Employee { get; set; }

        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();

        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutEnd { get; set; }

    }

    public enum UserType
    {
        [Display(Name = "مشتری")]
        Customer = 1,

        [Display(Name = "کارمند")]
        Employee = 2,
    }
}