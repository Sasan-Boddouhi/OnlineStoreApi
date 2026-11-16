using Application.Entities;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// Batch 4 (F-02) — ابزار مشترک تست برای مسیر واقعی «پرداخت ← ارسال ← تحویل».
///
/// از Batch 4 به بعد ارسال و تحویل فقط برای سفارش پرداخت‌شده ممکن است
/// و تحویل فقط پس از ارسال. تست‌هایی که پیش‌تر سفارش پرداخت‌نشده را مستقیم
/// «تحویل» می‌کردند، از این ابزار برای ساختن همان پیش‌شرط‌ها استفاده می‌کنند.
/// </summary>
public static class FulfilmentTestHelper
{
    /// <summary>
    /// یک پرداخت موفق (و در صورت نبود، فاکتور) برای سفارش ثبت می‌کند.
    /// وضعیت سفارش را تغییر نمی‌دهد؛ تأیید سفارش با <c>ConfirmOrderAsync</c> انجام می‌شود.
    /// پرداخت از مسیر ماشین حالت (Initiated → Pending → Succeeded) عبور می‌کند.
    /// </summary>
    public static async Task<Payment> AddSucceededPaymentAsync(AppDbContext db, int orderId)
    {
        var order = await db.Order.AsNoTracking().FirstAsync(o => o.OrderId == orderId);

        var invoice = await db.Invoice.FirstOrDefaultAsync(i => i.OrderId == orderId);
        if (invoice is null)
        {
            invoice = new Invoice
            {
                OrderId = orderId,
                InvoiceNumber = $"INV-B4-{Guid.NewGuid():N}",
                SubTotalAmount = order.TotalAmount,
                TotalAmount = order.TotalAmount
            };
            db.Invoice.Add(invoice);
            await db.SaveChangesAsync();
        }

        var payment = new Payment
        {
            Amount = order.TotalAmount,
            InvoiceId = invoice.InvoiceId,
            OrderId = orderId,
            Gateway = PaymentGatewayType.Manual,
            State = PaymentState.Initiated,
            TransactionId = $"B4-{Guid.NewGuid():N}"
        };
        payment.TransitionTo(PaymentState.Pending);
        payment.TransitionTo(PaymentState.Succeeded, "test: paid");

        db.Payment.Add(payment);
        await db.SaveChangesAsync();

        return payment;
    }

    /// <summary>
    /// همهٔ گروه‌های Processing سفارش را «ارسال‌شده» علامت می‌زند؛
    /// همان گذار دامنه‌ای که فروشنده با <c>ShipSellerGroupAsync</c> انجام می‌دهد.
    /// </summary>
    public static async Task ShipAllGroupsAsync(AppDbContext db, int orderId)
    {
        var groups = await db.OrderSellerGroup
            .AsTracking()
            .Where(g => g.OrderId == orderId)
            .ToListAsync();

        foreach (var group in groups.Where(g => g.Status == SellerGroupStatus.Processing))
            group.MarkShipped("TestPost", $"TRK-{group.OrderSellerGroupId}", DateTime.UtcNow);

        await db.SaveChangesAsync();
    }
}
