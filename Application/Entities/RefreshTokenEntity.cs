using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    [Table("RefreshToken")]
    public class RefreshTokenEntity

    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string TokenHash { get; set; } = string.Empty;

        [Required]
        public string TokenIdentifier { get; set; } = string.Empty;

        [Required]
        public Guid FamilyId { get; set; } = Guid.NewGuid();

        [Required]
        public DateTime FamilyCreatedAt { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = default!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Required]
        public DateTime ExpiryDate { get; set; }

        [Required]
        public bool IsRevoked { get; set; } = false;

        public DateTime? RevokedAtUtc { get; set; }

        public string? ReplacedByTokenHash { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }

        [Required]
        public DateTime AbsoluteExpiry { get; set; }
    }
}
