using BusinessLogic.DTOs.Payout;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface ISellerPayoutService
    {
        /// <summary>تسویه‌های فروشندهٔ جاری؛ برای Pending، کسر بدهی و مبلغ خالص پیش‌نمایش است (Batch 44).</summary>
        Task<IEnumerable<SellerPayoutDto>> GetMyPayoutsAsync(CancellationToken cancellationToken = default);

        /// <summary>همهٔ تسویه‌ها (ادمین)؛ برای Pending، کسر بدهی و مبلغ خالصِ قابل واریز پیش‌نمایش است (Batch 44).</summary>
        Task<IEnumerable<SellerPayoutDto>> GetAllPayoutsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// ثبت پرداخت تسویه. Batch 44: پیش از <c>AvailableAtUtc</c> → 409 PAYOUT_ON_HOLD؛ بدهی‌های باز فروشنده کسر و
        /// مبلغ خالص ثبت می‌شود. اگر <paramref name="expectedNetAmount"/> داده شود و با مبلغ خالص واقعی برابر نباشد
        /// (مثلاً بدهی تازه‌ای پس از دیدن فهرست ثبت شده) → 409 PAYOUT_NET_CHANGED و هیچ چیز ثبت نمی‌شود.
        /// </summary>
        Task<SellerPayoutDto> MarkAsPaidAsync(int payoutId, string? reference, decimal? expectedNetAmount = null, CancellationToken cancellationToken = default);

        Task<SellerPayoutDto> CancelPayoutAsync(int payoutId, CancellationToken cancellationToken = default);
    }
}
