using Application.Entities;

namespace BusinessLogic.Services.Interfaces;

/// <summary>Batch 41 — مسیری که سفارش را برمی‌گرداند؛ قواعد مجاز هر مسیر در <see cref="IOrderReversalService"/> آمده است.</summary>
public enum OrderReversalKind
{
    /// <summary>لغو توسط خودِ مشتری؛ پس از ارسال یا تحویل حتی یک بسته رد می‌شود (409 ORDER_PARTLY_SHIPPED).</summary>
    CustomerCancel = 0,

    /// <summary>لغو توسط ادمین (پشتیبانی)؛ سفارش ارسال‌شده/تحویل‌شده/مرجوعی لغو نمی‌شود.</summary>
    AdminCancel = 1,

    /// <summary>برگشت وجه ادمین: سفارش تحویل‌شده مرجوع و سفارش پیش از ارسال لغو می‌شود؛ سفارشِ در راه دست نمی‌خورد.</summary>
    AdminRefund = 2,

    /// <summary>مرجوعی سفارش تحویل‌شده توسط ادمین.</summary>
    AdminReturn = 3,

    /// <summary>جاب سفارش‌های رهاشده؛ هرگز برای سفارشی که پرداخت موفق دارد اجرا نمی‌شود.</summary>
    AbandonedOrder = 4
}

/// <summary>Batch 41 — خلاصهٔ آنچه یک بار اجرای روال برگرداندن سفارش تغییر داد.</summary>
/// <param name="PreviousStatus">وضعیت سفارش پیش از اجرا.</param>
/// <param name="Status">وضعیت سفارش پس از اجرا (اگر سفارش دست نخورده باشد همان قبلی).</param>
/// <param name="RefundRequestedPaymentIds">پرداخت‌هایی که به RefundPending رفتند.</param>
/// <param name="CancelledOpenPayments">تعداد پرداخت‌های بازِ لغوشده.</param>
/// <param name="CancelledPayouts">تعداد تسویه‌های Pending لغوشده.</param>
/// <param name="ClawbackDebts">تعداد بدهی‌های ساخته‌شده برای تسویه‌های Paid.</param>
public sealed record OrderReversalResult(
    OrderStatus PreviousStatus,
    OrderStatus Status,
    IReadOnlyList<int> RefundRequestedPaymentIds,
    int CancelledOpenPayments,
    int CancelledPayouts,
    int ClawbackDebts)
{
    /// <summary>آیا وضعیت سفارش در همین اجرا تغییر کرد.</summary>
    public bool OrderChanged => PreviousStatus != Status;
}

/// <summary>
/// Batch 41 (FIN-01، FIN-03) — تنها روال «برگرداندن سفارش». لغو مشتری، لغو ادمین، برگشت وجه ادمین، مرجوعی ادمین و
/// جاب سفارش‌های رهاشده همه از همین‌جا می‌گذرند تا گروه‌های فروشنده، موجودی، سهمیهٔ کوپن، تسویه‌ها، بدهی‌های
/// بازپس‌گیری و پرداخت‌ها یک بار و یک‌جور نوشته شوند.
/// </summary>
public interface IOrderReversalService
{
    /// <summary>
    /// سفارش را برمی‌گرداند. همهٔ بررسی‌ها پیش از هر تغییری انجام می‌شود؛ اگر رد شود هیچ‌چیز تغییر نکرده است.
    /// ذخیره (SaveChanges) و تراکنش با فراخوان است.
    /// </summary>
    /// <param name="order">سفارشِ ردیابی‌شده، همراه با OrderItems و SellerGroups.</param>
    /// <param name="kind">مسیر فراخوان.</param>
    /// <param name="reason">دلیل؛ روی پرداخت (RefundReason) و در AuditLog ثبت می‌شود.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    Task<OrderReversalResult> ReverseAsync(
        Order order,
        OrderReversalKind kind,
        string reason,
        CancellationToken cancellationToken = default);
}
