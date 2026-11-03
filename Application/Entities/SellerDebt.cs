using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// M7.12 — بدهی‌های فروشنده ناشی از بازگشت کالای سفارش‌های تسویه‌شده (Clawback).
    /// </summary>
    [Table("SellerDebt")]
    public class SellerDebt : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SellerDebtId { get; set; }

        public int SellerId { get; set; }

        [ForeignKey(nameof(SellerId))]
        public virtual Seller Seller { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required, MaxLength(500)]
        public string Reason { get; set; } = null!;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsSettled { get; set; } = false;
    }
}
