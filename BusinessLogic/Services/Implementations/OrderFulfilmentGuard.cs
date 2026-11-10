using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 4 (F-02) — قواعد مشترک ارسال و تحویل گروه فروشنده.
///
/// <para>
/// تعریف «سفارش پرداخت‌شده» (تنها منبع حقیقت):
/// سفارش در یکی از وضعیت‌های Processing/Shipped/Delivered است
/// <b>و</b> دست‌کم یک <see cref="Payment"/> با وضعیت <see cref="PaymentState.Succeeded"/> دارد.
/// </para>
/// <para>
/// وضعیت سفارش به‌تنهایی کافی نیست: تأیید ادمین (<c>ConfirmOrderAsync</c>) سفارش را
/// بی‌پرداخت به Processing می‌برد. پرداخت موفق به‌تنهایی هم کافی نیست: ثبت دستی پرداخت
/// سفارش را از Pending بیرون نمی‌آورد و برگشت وجه پرداخت را Refunded می‌کند.
/// </para>
/// <para>
/// همهٔ رد شدن‌ها <see cref="ConflictException"/> (409) هستند تا هیچ
/// InvalidOperationException دامنه‌ای به 500 تبدیل نشود.
/// </para>
/// </summary>
internal static class OrderFulfilmentGuard
{
    /// <summary>
    /// ارسال یا تحویل را برای سفارشی که پرداخت‌شده نیست، یا لغو/مرجوع/برگشت وجه شده، رد می‌کند.
    /// </summary>
    public static async Task EnsureOrderIsPaidAsync(
        IUnitOfWork unitOfWork,
        Order order,
        CancellationToken cancellationToken)
    {
        if (order.Status == OrderStatus.Cancelled)
            throw new ConflictException("این سفارش لغو شده است و قابل ارسال یا تحویل نیست.", "ORDER_CANCELLED");

        if (order.Status == OrderStatus.Returned)
            throw new ConflictException("این سفارش مرجوع شده است و قابل ارسال یا تحویل نیست.", "ORDER_RETURNED");

        var paymentStates = await unitOfWork.Repository<Payment>()
            .Query()
            .Where(p => p.OrderId == order.OrderId)
            .Select(p => p.State)
            .ToListAsync(cancellationToken);

        if (!paymentStates.Contains(PaymentState.Succeeded))
        {
            if (paymentStates.Contains(PaymentState.Refunded))
                throw new ConflictException("وجه این سفارش به مشتری برگشت داده شده است و قابل ارسال یا تحویل نیست.", "ORDER_REFUNDED");

            throw new ConflictException("این سفارش هنوز پرداخت نشده است و قابل ارسال یا تحویل نیست.", "ORDER_NOT_PAID");
        }

        // پرداخت موفق دارد ولی هنوز به چرخهٔ پردازش وارد نشده (مثلاً ثبت دستی پرداخت بی‌تأیید).
        if (order.Status == OrderStatus.Pending)
            throw new ConflictException("این سفارش هنوز پرداخت و تأیید نهایی نشده است و قابل ارسال یا تحویل نیست.", "ORDER_NOT_PAID");
    }

    /// <summary>ارسال فقط از Processing (آینهٔ گارد <see cref="OrderSellerGroup.MarkShipped"/>).</summary>
    public static void EnsureGroupCanBeShipped(OrderSellerGroup group)
    {
        if (group.Status != SellerGroupStatus.Processing)
            throw new ConflictException(
                $"فقط بستهٔ در حال پردازش قابل ارسال است؛ وضعیت فعلی بسته: {group.Status}.",
                "GROUP_NOT_PROCESSING");
    }

    /// <summary>تحویل فقط از Shipped (آینهٔ گارد <see cref="OrderSellerGroup.MarkDelivered"/>).</summary>
    public static void EnsureGroupCanBeDelivered(OrderSellerGroup group)
    {
        if (group.Status != SellerGroupStatus.Shipped)
            throw new ConflictException(
                $"این بسته هنوز ارسال نشده است و قابل تحویل نیست؛ وضعیت فعلی بسته: {group.Status}.",
                "GROUP_NOT_SHIPPED");
    }

    /// <summary>
    /// برای گروه تحویل‌شده، اگر تسویه‌ای ندارد، یک SellerPayout می‌سازد (یک تسویه به‌ازای هر گروه).
    /// فراخواننده باید پیش‌تر <see cref="EnsureOrderIsPaidAsync"/> را گذرانده باشد.
    /// ایندکس یکتای SellerPayout.OrderSellerGroupId آخرین سد در برابر درخواست‌های هم‌زمان است.
    /// </summary>
    /// <returns><c>true</c> اگر تسویهٔ تازه‌ای اضافه شد.</returns>
    public static async Task<bool> AddPayoutIfMissingAsync(
        IUnitOfWork unitOfWork,
        OrderSellerGroup group,
        DateTime periodStart,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (group.Status != SellerGroupStatus.Delivered)
            throw new InvalidOperationException("A payout can only be created for a delivered group.");

        var exists = await unitOfWork.Repository<SellerPayout>()
            .AnyAsync(p => p.OrderSellerGroupId == group.OrderSellerGroupId, cancellationToken);

        if (exists)
            return false;

        var payout = new SellerPayout(
            group.SellerId,
            group.OrderSellerGroupId,
            group.SellerPayable,
            periodStart,
            now);

        await unitOfWork.Repository<SellerPayout>().AddAsync(payout, cancellationToken);
        return true;
    }

    /// <summary>
    /// ذخیرهٔ تغییرات ارسال/تحویل. تداخل هم‌زمان (RowVersion گروه/سفارش یا ایندکس یکتای
    /// تسویهٔ همان گروه) به 409 تبدیل می‌شود، نه 500.
    /// </summary>
    /// <param name="unitOfWork">واحد کار جاری.</param>
    /// <param name="payoutGroupIds">گروه‌هایی که در این ذخیره برایشان تسویهٔ تازه اضافه شده است.</param>
    /// <param name="cancellationToken">توکن لغو.</param>
    public static async Task SaveAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<int> payoutGroupIds,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
        }
        catch (DbUpdateException) when (payoutGroupIds.Count > 0)
        {
            // درخواست دیگری هم‌زمان همین گروه را تحویل داده و تسویه‌اش را ساخته است
            // (ایندکس یکتای SellerPayout.OrderSellerGroupId). خطای دیگر دیتابیس پنهان نمی‌شود.
            if (!await PayoutAlreadyExistsAsync(unitOfWork, payoutGroupIds))
                throw;

            throw new ConflictException("این بسته هم‌زمان توسط درخواست دیگری تحویل شد. لطفاً وضعیت را دوباره بررسی کنید.", "CONCURRENCY_CONFLICT");
        }
    }

    private static Task<bool> PayoutAlreadyExistsAsync(IUnitOfWork unitOfWork, IReadOnlyCollection<int> groupIds)
        => unitOfWork.Repository<SellerPayout>()
            .Query()
            .AsNoTracking()
            .AnyAsync(p => p.OrderSellerGroupId != null && groupIds.Contains(p.OrderSellerGroupId.Value));
}
