using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("Customer")]
    public class Customer : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CustomerId { get; set; }

        // -------- Loyalty & Stats --------
        public int Points { get; set; } = 0;
        public LoyaltyLevel LoyaltyLevel { get; set; } = LoyaltyLevel.Bronze;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalSpent { get; set; } = 0;
        public int OrderCount { get; set; } = 0;

        // -------- Tracking --------
        public DateTime? LastLoginDate { get; set; }
        public DateTime? LastPurchaseDate { get; set; }
        public bool IsBlocked { get; set; } = false;
        public int FailedLoginAttempts { get; set; } = 0;

        // Required 1:1 relationship with User
        [Required]
        public required int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
        public virtual ICollection<Order> Orders { get; set; } = new HashSet<Order>();
    }

    public enum LoyaltyLevel
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
        Platinum = 3,
        VIP = 4
    }
}