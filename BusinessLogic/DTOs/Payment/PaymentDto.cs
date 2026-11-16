using Application.Entities;

namespace BusinessLogic.DTOs.Payment
{
    public class PaymentDto
    {
        public int PaymentId { get; set; }
        public int InvoiceId { get; set; }
        public int? OrderId { get; set; }
        public decimal Amount { get; set; }
        public string? TransactionId { get; set; }
        public DateTime PaymentDate { get; set; }

        // ---- Phase 8 ----
        public PaymentState State { get; set; }
        public string StateName => State.ToString();
        public PaymentGatewayType Gateway { get; set; }
        public DateTime LastStateChangedAtUtc { get; set; }

        // ---- Batch 42 (FIN-12): برگشت وجه ----

        /// <summary>لحظهٔ ثبت درخواست برگشت وجه (ورود به RefundPending).</summary>
        public DateTime? RefundRequestedAtUtc { get; set; }

        /// <summary>تاریخ برگشت واقعی پول (پس از تأیید ادمین).</summary>
        public DateTime? RefundedAtUtc { get; set; }

        /// <summary>شمارهٔ پیگیری بانکی برگشت وجه.</summary>
        public string? RefundReference { get; set; }

        /// <summary>دلیل برگشت وجه.</summary>
        public string? RefundReason { get; set; }

        // ---- Batch 43 (FIN-07): جزئیات درگاه ----
        // PaymentDto به مشتری هم برگردانده می‌شود. کارمزد درگاه هزینهٔ داخلی فروشگاه است و فقط برای ادمین پر می‌شود.
        // کارت ماسک‌شده (۶ رقم اول + ۴ رقم آخر) را ادمین و، به تصمیم مالک، خودِ صاحب پرداخت می‌بیند؛ هیچ کاربر دیگری نه.

        /// <summary>کارمزد درگاه برای این تراکنش (همان عدد پاسخ درگاه). فقط ادمین؛ برای بقیه null.</summary>
        public decimal? GatewayFee { get; set; }

        /// <summary>پرداخت‌کنندهٔ کارمزد به گفتهٔ درگاه (مثلاً Merchant). فقط ادمین؛ برای بقیه null.</summary>
        public string? GatewayFeeType { get; set; }

        /// <summary>
        /// شمارهٔ کارت ماسک‌شدهٔ پرداخت‌کننده (مثلاً 502229******5995). برای ادمین و برای مشتری روی پرداختِ سفارش
        /// خودش (پاسخ verify و GET payments/order/{orderId}) پر می‌شود؛ در غیر این صورت null.
        /// </summary>
        public string? CardPanMasked { get; set; }
    }
}