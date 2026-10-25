using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{
    /// <summary>
    /// Phase 8 — پرداخت.
    ///
    /// قواعد سخت:
    /// 1) هیچ اطلاعات کارت (PAN/CVV/تاریخ انقضا) اینجا ذخیره نمی‌شود؛
    ///    Tokenization کار درگاه است و ما فقط Authority دریافت می‌کنیم.
    /// 2) تغییر وضعیت فقط از مسیر <see cref="TransitionTo"/> ممکن است،
    ///    تا انتقال غیرمجاز (مثل Pending → Succeeded بی‌تأیید درگاه) ممکن نباشد.
    /// 3) IdempotencyKey در سطح دیتابیس یکتاست، نه فقط در کد.
    /// </summary>
    [Table("Payment")]
    public class Payment : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PaymentId { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        [Required]
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CreditCard;

        /// <summary>شناسهٔ تراکنش در سمت درگاه (Authority).</summary>
        [MaxLength(100)]
        public string? TransactionId { get; set; }

        /// <summary>کد تأیید نهایی (Reference ID) که درگاه پس از پرداخت موفق می‌دهد.</summary>
        [MaxLength(100)]
        public string? GatewayReference { get; set; }


        [MaxLength(500)]
        public string? Notes { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        // ---------------- Phase 8 ----------------

        /// <summary>ماشین حالت واقعی پرداخت.</summary>
        [Required]
        public PaymentState State { get; set; } = PaymentState.Initiated;

        /// <summary>لحظهٔ آخرین تغییر وضعیت؛ برای تشخیص پرداخت‌های رهاشده.</summary>
        public DateTime LastStateChangedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>درگاهی که این پرداخت را انجام می‌دهد.</summary>
        [Required]
        public PaymentGatewayType Gateway { get; set; } = PaymentGatewayType.Manual;

        /// <summary>
        /// کلید یکتای idempotency.
        /// اگر کلاینت دو بار درخواست بدهد، همان پرداخت قبلی برگردانده می‌شود.
        /// </summary>
        [MaxLength(64)]
        public string? IdempotencyKey { get; set; }

        /// <summary>سفارشی که این پرداخت برایش انجام می‌شود.</summary>
        public int? OrderId { get; set; }

        [ForeignKey(nameof(OrderId))]
        public virtual Order? Order { get; set; }

        [Required]
        public int InvoiceId { get; set; }

        [ForeignKey("InvoiceId")]
        public virtual Invoice Invoice { get; set; } = null!;

        /// <summary>
        /// انتقال وضعیت با اعتبارسنجی.
        ///
        /// مسیر مجاز:
        ///   Initiated → Pending | Failed | Cancelled
        ///   Pending   → Succeeded | Failed | Cancelled
        ///   Succeeded → Refunded
        /// و هیچ‌چیز از Failed/Cancelled/Refunded به عقب برنمی‌گردد.
        /// </summary>
        public void TransitionTo(PaymentState next, string? note = null)
        {
            if (!IsAllowed(State, next))
                throw new InvalidOperationException($"Invalid payment transition: {State} → {next}.");

            State = next;
            LastStateChangedAtUtc = DateTime.UtcNow;

            if (note is not null)
                Notes = note;


        }

        public static bool IsAllowed(PaymentState from, PaymentState to) => (from, to) switch
        {
            (PaymentState.Initiated, PaymentState.Pending) => true,
            (PaymentState.Initiated, PaymentState.Failed) => true,
            (PaymentState.Initiated, PaymentState.Cancelled) => true,
            (PaymentState.Pending, PaymentState.Succeeded) => true,
            (PaymentState.Pending, PaymentState.Failed) => true,
            (PaymentState.Pending, PaymentState.Cancelled) => true,
            (PaymentState.Succeeded, PaymentState.Refunded) => true,
            _ => false
        };
    }


    public enum PaymentMethod
    {
        CreditCard = 0,
        Cash = 1,
        BankTransfer = 2,
        BankCheck = 3,
        PersonalCheck = 4
    }

    /// <summary>Phase 8 — ماشین حالت صریح پرداخت.</summary>
    public enum PaymentState
    {
        Initiated = 0,
        Pending = 1,
        Succeeded = 2,
        Failed = 3,
        Cancelled = 4,
        Refunded = 5
    }

    /// <summary>Phase 8 — درگاه‌های پشتیبانی‌شده.</summary>
    public enum PaymentGatewayType
    {
        /// <summary>پرداخت دستی/آفلاین (تست و تسویهٔ خارج از سیستم).</summary>
        Manual = 0,
        Zarinpal = 1,
        IDPay = 2
    }
}