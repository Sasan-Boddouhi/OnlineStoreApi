using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// M7.12 — بدهی‌های فروشنده ناشی از بازگشت کالای سفارش‌های تسویه‌شده (Clawback).
    /// <para>
    /// Batch 44 (F-09): مبلغ اصلی بدهی (<see cref="OriginalAmount"/>) هرگز تغییر نمی‌کند. هر بار که بخشی از آن از
    /// تسویهٔ فروشنده کسر شود <see cref="SettledAmount"/> بالا می‌رود؛ وقتی به مبلغ اصلی برسد بدهی تسویه‌شده
    /// (<see cref="IsSettled"/>) و <see cref="SettledAtUtc"/> ثبت می‌شود. پیش‌تر خودِ مبلغ کم می‌شد و تاریخچه از دست می‌رفت.
    /// </para>
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

        private decimal _originalAmount;

        /// <summary>
        /// Batch 44: مبلغ اصلی بدهی هنگام ثبت (پیش‌تر ستون Amount). تغییر نمی‌کند.
        /// Batch 45: مقداردهی آن باقی‌مانده را هم (= مبلغ اصلی − تسویه‌شده) تنظیم می‌کند؛ EF از فیلد پشتیبان می‌خواند.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal OriginalAmount
        {
            get => _originalAmount;
            set
            {
                _originalAmount = value;
                RemainingAmount = value - SettledAmount;
            }
        }

        /// <summary>Batch 44: جمع مبالغی که تاکنون از تسویه‌های فروشنده بابت این بدهی کسر شده است.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal SettledAmount { get; private set; }

        /// <summary>Batch 44: لحظهٔ تسویهٔ کامل بدهی (تا آن زمان null).</summary>
        public DateTime? SettledAtUtc { get; private set; }

        /// <summary>
        /// Batch 45 (اصلاحیهٔ مالک ۲): باقی‌ماندهٔ بدهی به‌صورت ستون صریح؛ همیشه = مبلغ اصلی − تسویه‌شده.
        /// کسر بدهی از تسویه از روی همین ستون است (نه حدس «تغییر بعد از ساخت» در مهاجرت Batch 44).
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal RemainingAmount { get; private set; }

        [Required, MaxLength(500)]
        public string Reason { get; set; } = null!;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsSettled { get; private set; } = false;

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[8];

        /// <summary>
        /// Batch 44: کسر <paramref name="amount"/> از باقی‌ماندهٔ بدهی (۰ &lt; amount ≤ باقی‌مانده).
        /// با رسیدن باقی‌مانده به صفر بدهی تسویه‌شده می‌شود.
        /// </summary>
        public void ApplySettlement(decimal amount, DateTime nowUtc)
        {
            if (IsSettled)
                throw new InvalidOperationException("The debt is already settled.");

            if (amount <= 0 || amount > RemainingAmount)
                throw new ArgumentOutOfRangeException(nameof(amount), "Settlement must be positive and not exceed the remaining debt.");

            SettledAmount += amount;
            RemainingAmount -= amount;

            if (RemainingAmount == 0)
            {
                IsSettled = true;
                SettledAtUtc = nowUtc;
            }
        }
    }
}
