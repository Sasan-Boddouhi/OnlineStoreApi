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
    ///    Tokenization کار درگاه است و ما فقط Authority دریافت می‌کنیم. تنها استثنا (Batch 43) شمارهٔ کارتِ
    ///    <b>ماسک‌شده</b> است که خودِ درگاه پس از پرداخت برمی‌گرداند (<see cref="CardPanMasked"/>).
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

        // ---------------- Batch 42 ----------------

        /// <summary>
        /// Batch 42 (FIN-12): لحظه‌ای که این پرداخت برای بررسی دستی علامت خورد (برداشت پول برای سفارش ناپرداختنی،
        /// تأییدِ بی‌نتیجه، ناسازگاری پس از برداشت). تا وقتی <see cref="ReviewResolvedAtUtc"/> خالی است، بررسی باز است.
        /// رکورد <c>Payment.ReviewRequired</c> در AuditLog همچنان نوشته می‌شود و فقط مدرک است.
        /// </summary>
        public DateTime? ReviewRequiredAtUtc { get; set; }

        /// <summary>Batch 42: شرح مشکل (چند مشکلِ پیاپی پشت هم افزوده می‌شوند).</summary>
        [MaxLength(1000)]
        public string? ReviewNote { get; set; }

        /// <summary>Batch 42: لحظهٔ بسته شدن بررسی توسط ادمین.</summary>
        public DateTime? ReviewResolvedAtUtc { get; set; }

        /// <summary>Batch 42: ادمینی که بررسی را بست.</summary>
        public int? ReviewResolvedByUserId { get; set; }

        /// <summary>Batch 42: بررسی دستی باز است.</summary>
        [NotMapped]
        public bool IsReviewOpen => ReviewRequiredAtUtc is not null && ReviewResolvedAtUtc is null;

        // ---------------- Batch 43 ----------------

        /// <summary>
        /// Batch 43 (FIN-07): کارمزد درگاه برای این تراکنش، همان عددی که درگاه در پاسخ Verify برگردانده است
        /// (برای تطبیق با گزارش تسویهٔ درگاه). پرداخت‌های پیش از این Batch مقدار ندارند.
        /// </summary>
        [Column(TypeName = "decimal(18,0)")]
        public decimal? GatewayFee { get; set; }

        /// <summary>Batch 43 (FIN-07): پرداخت‌کنندهٔ کارمزد به گفتهٔ درگاه (مثلاً Merchant یا Payer).</summary>
        [MaxLength(20)]
        public string? GatewayFeeType { get; set; }

        /// <summary>
        /// Batch 43 (FIN-07): شمارهٔ کارت <b>ماسک‌شدهٔ</b> پرداخت‌کننده (حداکثر ۶ رقم اول و ۴ رقم آخر، مثلاً
        /// 502229******5995) برای پشتیبانی و تطبیق. قاعدهٔ ۱ همچنان برقرار است: شمارهٔ کامل کارت هرگز ذخیره
        /// نمی‌شود؛ مقدار فقط از <see cref="RecordGatewayDetails"/> نوشته می‌شود که خودش دوباره ماسک می‌کند.
        /// </summary>
        [MaxLength(20)]
        public string? CardPanMasked { get; set; }

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
        /// <param name="nowUtc">لحظهٔ ثبت در سیستم.</param>
        /// <param name="reference">شمارهٔ پیگیری بانکی برگشت.</param>
        /// <param name="note">یادداشت اختیاری.</param>
        /// <param name="refundedAtUtc">Batch 42: تاریخ واقعی برگشت پول در بانک؛ اگر داده نشود همان <paramref name="nowUtc"/>.</param>
        public void ConfirmRefund(DateTime nowUtc, string? reference = null, string? note = null, DateTime? refundedAtUtc = null)
        {
            TransitionTo(PaymentState.Refunded, note is null ? null : Truncate(note, 500), nowUtc);

            RefundedAtUtc = refundedAtUtc ?? nowUtc;

            if (!string.IsNullOrWhiteSpace(reference))
                RefundReference = Truncate(reference.Trim(), 100);
        }

        /// <summary>
        /// Batch 42 (FIN-12): پرداخت را برای بررسی دستی علامت می‌زند. اگر بررسیِ باز دارد، شرح تازه به آن افزوده
        /// می‌شود؛ اگر بررسی قبلی بسته شده، بررسی تازه‌ای باز می‌شود.
        /// </summary>
        public void FlagForReview(DateTime nowUtc, string note)
        {
            var text = string.IsNullOrWhiteSpace(note) ? "review required" : note.Trim();

            if (IsReviewOpen)
            {
                ReviewNote = Truncate(string.IsNullOrEmpty(ReviewNote) ? text : $"{ReviewNote} || {text}", 1000);
                return;
            }

            ReviewRequiredAtUtc = nowUtc;
            ReviewNote = Truncate(text, 1000);
            ReviewResolvedAtUtc = null;
            ReviewResolvedByUserId = null;
        }

        /// <summary>Batch 42 (FIN-12): ادمین بررسی باز را می‌بندد. بررسیِ باز نداشتن ⇒ 409.</summary>
        public void ResolveReview(DateTime nowUtc, int? resolvedByUserId, string note)
        {
            if (!IsReviewOpen)
                throw new BusinessRuleViolationException("This payment has no open review.", "PAYMENT_REVIEW_NOT_OPEN");

            ReviewResolvedAtUtc = nowUtc;
            ReviewResolvedByUserId = resolvedByUserId;

            if (!string.IsNullOrWhiteSpace(note))
                ReviewNote = Truncate(string.IsNullOrEmpty(ReviewNote) ? $"resolved: {note.Trim()}" : $"{ReviewNote} || resolved: {note.Trim()}", 1000);
        }

        /// <summary>
        /// Batch 43 (FIN-07): جزئیاتی که درگاه در پاسخ Verify داده است. مقدار خالی چیزی را پاک نمی‌کند (تأیید تکراری
        /// ممکن است جزئیات کمتری برگرداند). شمارهٔ کارت همیشه از <see cref="MaskCardPan"/> می‌گذرد.
        /// </summary>
        public void RecordGatewayDetails(decimal? fee, string? feeType, string? cardPan)
        {
            if (fee is >= 0)
                GatewayFee = decimal.Truncate(fee.Value);

            if (!string.IsNullOrWhiteSpace(feeType))
                GatewayFeeType = Truncate(feeType.Trim(), 20);

            var masked = MaskCardPan(cardPan);
            if (masked is not null)
                CardPanMasked = masked;
        }

        /// <summary>
        /// Batch 43: شمارهٔ کارت را به شکل امنِ قابل ذخیره درمی‌آورد: فقط ۶ رقم اول و ۴ رقم آخر می‌مانند و بقیه <c>*</c>
        /// می‌شوند، حتی اگر درگاه شمارهٔ کامل یا ماسکِ ضعیف‌تری فرستاده باشد. ورودی نامعتبر (طول نادرست یا نویسهٔ
        /// غیرمنتظره) ⇒ null و چیزی ذخیره نمی‌شود.
        /// </summary>
        public static string? MaskCardPan(string? cardPan)
        {
            if (string.IsNullOrWhiteSpace(cardPan))
                return null;

            var compact = cardPan.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (compact.Length is < 12 or > 19)
                return null;

            var chars = new char[compact.Length];
            for (var i = 0; i < compact.Length; i++)
            {
                var c = compact[i];
                var isDigit = c is >= '0' and <= '9';
                if (!isDigit && c is not ('*' or 'x' or 'X'))
                    return null;

                var visible = i < 6 || i >= compact.Length - 4;
                chars[i] = isDigit && visible ? c : '*';
            }

            return new string(chars);
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