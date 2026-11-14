using Application.Exceptions;
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

        // ---------------- Batch 41 ----------------

        /// <summary>
        /// Batch 41 (FIN-11): لحظه‌ای که Verify درگاه برای این پرداخت آغاز شد.
        /// پرداخت Pending که این مقدار را دارد «تأییدِ نامعلوم» است: شاید پول برداشته شده باشد و هرگز بی‌صدا لغو نمی‌شود.
        /// رکورد <c>Payment.VerificationStarted</c> در AuditLog فقط مدرک است، نه منبع تصمیم.
        /// </summary>
        public DateTime? VerifyStartedAtUtc { get; set; }

        /// <summary>Batch 41 (FIN-01): لحظهٔ ثبت درخواست برگشت وجه (ورود به <see cref="PaymentState.RefundPending"/>).</summary>
        public DateTime? RefundRequestedAtUtc { get; set; }

        /// <summary>Batch 41 (FIN-01): لحظه‌ای که ادمین برگشت واقعی پول را تأیید کرد (ورود به <see cref="PaymentState.Refunded"/>).</summary>
        public DateTime? RefundedAtUtc { get; set; }

        /// <summary>Batch 41 (FIN-01): شمارهٔ پیگیری بانکیِ برگشت وجه (در Batch 42 هنگام تأیید پر می‌شود).</summary>
        [MaxLength(100)]
        public string? RefundReference { get; set; }

        /// <summary>Batch 41 (FIN-01): دلیل برگشت وجه (لغو سفارش، مرجوعی، برگشت دستی ادمین).</summary>
        [MaxLength(500)]
        public string? RefundReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = new byte[8];

        /// <summary>
        /// انتقال وضعیت با اعتبارسنجی.
        ///
        /// مسیر مجاز:
        ///   Initiated     → Pending | Failed | Cancelled
        ///   Pending       → Succeeded | Failed | Cancelled
        ///   Succeeded     → RefundPending            (Batch 41: درخواست برگشت وجه؛ <see cref="RequestRefund"/>)
        ///   RefundPending → Refunded                 (Batch 41: تأیید برگشت واقعی پول؛ <see cref="ConfirmRefund"/>)
        /// و هیچ‌چیز از Failed/Cancelled/Refunded به عقب برنمی‌گردد. میان‌بر Succeeded → Refunded وجود ندارد:
        /// پرداختی که پولش هنوز برنگشته هرگز «برگشت‌داده‌شده» نشان داده نمی‌شود.
        /// </summary>
        /// <param name="next">وضعیت مقصد.</param>
        /// <param name="note">یادداشت اختیاری (در <see cref="Notes"/> ذخیره می‌شود).</param>
        /// <param name="nowUtc">لحظهٔ تغییر از ساعت تزریق‌شده (TimeProvider)؛ اگر داده نشود ساعت سیستم.</param>
        public void TransitionTo(PaymentState next, string? note = null, DateTime? nowUtc = null)
        {
            if (!IsAllowed(State, next))
                throw new BusinessRuleViolationException($"Invalid payment transition: {State} → {next}.", "PAYMENT_INVALID_TRANSITION");

            State = next;
            LastStateChangedAtUtc = nowUtc ?? DateTime.UtcNow;

            if (note is not null)
                Notes = note;


        }

        /// <summary>
        /// Batch 41 (FIN-01): پول این پرداخت باید به مشتری برگردد (لغو سفارش پرداخت‌شده، مرجوعی، برگشت دستی ادمین).
        /// فقط از Succeeded؛ پرداخت در صف برگشت وجه می‌ماند تا برگشت واقعی پول با <see cref="ConfirmRefund"/> تأیید شود.
        /// </summary>
        public void RequestRefund(DateTime nowUtc, string reason)
        {
            TransitionTo(PaymentState.RefundPending, nowUtc: nowUtc);

            RefundRequestedAtUtc = nowUtc;
            RefundReason = Truncate(string.IsNullOrWhiteSpace(reason) ? "refund requested" : reason.Trim(), 500);
        }

        /// <summary>
        /// Batch 41 (FIN-01): ادمین تأیید می‌کند که پول واقعاً به مشتری برگشته است. فقط از RefundPending.
        /// </summary>
        public void ConfirmRefund(DateTime nowUtc, string? reference = null, string? note = null)
        {
            TransitionTo(PaymentState.Refunded, note is null ? null : Truncate(note, 500), nowUtc);

            RefundedAtUtc = nowUtc;

            if (!string.IsNullOrWhiteSpace(reference))
                RefundReference = Truncate(reference.Trim(), 100);
        }

        private static string Truncate(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength];

        public static bool IsAllowed(PaymentState from, PaymentState to) => (from, to) switch
        {
            (PaymentState.Initiated, PaymentState.Pending) => true,
            (PaymentState.Initiated, PaymentState.Failed) => true,
            (PaymentState.Initiated, PaymentState.Cancelled) => true,
            (PaymentState.Pending, PaymentState.Succeeded) => true,
            (PaymentState.Pending, PaymentState.Failed) => true,
            (PaymentState.Pending, PaymentState.Cancelled) => true,
            (PaymentState.Succeeded, PaymentState.RefundPending) => true,
            (PaymentState.RefundPending, PaymentState.Refunded) => true,
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
        Refunded = 5,

        /// <summary>
        /// Batch 41 (FIN-01): پول باید به مشتری برگردد ولی هنوز برنگشته است (کار باز برای اپراتور).
        /// فقط پس از تأیید برگشت واقعی پول به <see cref="Refunded"/> می‌رود.
        /// </summary>
        RefundPending = 6
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