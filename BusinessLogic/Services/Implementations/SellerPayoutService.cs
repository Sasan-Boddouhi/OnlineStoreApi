using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Payout;
using BusinessLogic.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    /// <summary>
    /// تسویهٔ فروشنده. Batch 44 (FIN-02، F-09):
    /// <list type="bullet">
    /// <item>نگه‌داشت: پرداخت پیش از <see cref="SellerPayout.AvailableAtUtc"/> (تحویل + PayoutSettings:HoldDays) → 409 PAYOUT_ON_HOLD.</item>
    /// <item>کسر بدهی: بدهی‌های باز فروشنده (قدیمی‌ترین اول) از مبلغ کم می‌شود؛ کسر و خالص روی تسویه ثبت می‌شود و
    /// فهرست‌ها پیش از پرداخت همان خالص را نشان می‌دهند، پس ادمین دقیقاً مبلغ خالص را واریز می‌کند.</item>
    /// <item>مبلغ بدهی کم نمی‌شود؛ مبلغ تسویه‌شده‌اش بالا می‌رود (OriginalAmount ثابت می‌ماند).</item>
    /// <item>همهٔ رد شدن‌های وضعیت با یک کد: PAYOUT_INVALID_STATE (409).</item>
    /// </list>
    /// </summary>
    public sealed class SellerPayoutService : ISellerPayoutService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;
        private readonly ILogger<SellerPayoutService> _logger;
        private readonly IAuditService? _auditService;
        private readonly TimeProvider _timeProvider;

        public SellerPayoutService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IMapper mapper,
            ILogger<SellerPayoutService> logger,
            IAuditService? auditService = null,
            TimeProvider? timeProvider = null)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _mapper = mapper;
            _logger = logger;
            _auditService = auditService;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<IEnumerable<SellerPayoutDto>> GetMyPayoutsAsync(CancellationToken cancellationToken = default)
        {
            var userId = _currentUser.TryGetCurrentUserId();
            if (userId is null) return Enumerable.Empty<SellerPayoutDto>();

            var seller = await _unitOfWork.Repository<Seller>()
                .FirstOrDefaultAsync(new Spec<Seller>().Where(s => s.UserId == userId.Value), cancellationToken);
            if (seller is null) return Enumerable.Empty<SellerPayoutDto>();

            var payouts = await _unitOfWork.Repository<SellerPayout>()
                .ListAsync(new Spec<SellerPayout>().Where(p => p.SellerId == seller.SellerId), cancellationToken);

            return await ToDtosWithPreviewAsync(payouts, cancellationToken);
        }

        public async Task<IEnumerable<SellerPayoutDto>> GetAllPayoutsAsync(CancellationToken cancellationToken = default)
        {
            var payouts = await _unitOfWork.Repository<SellerPayout>()
                .ListAsync(new Spec<SellerPayout>(), cancellationToken);
            return await ToDtosWithPreviewAsync(payouts, cancellationToken);
        }

        public async Task<SellerPayoutDto> MarkAsPaidAsync(
            int payoutId,
            string? reference,
            decimal? expectedNetAmount = null,
            CancellationToken cancellationToken = default)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var payout = await _unitOfWork.Repository<SellerPayout>().FirstOrDefaultAsync(
                    new Spec<SellerPayout>().Where(p => p.SellerPayoutId == payoutId).AsTracking(),
                    cancellationToken);
                if (payout == null)
                    throw new NotFoundException("تسویه یافت نشد.", "PAYOUT_NOT_FOUND");

                if (payout.Status != PayoutStatus.Pending)
                    throw new ConflictException("فقط تسویه‌های در انتظار قابل پرداخت هستند.", "PAYOUT_INVALID_STATE");

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (payout.IsOnHold(now))
                    throw new ConflictException(
                        $"این تسویه هنوز در دورهٔ نگه‌داشت (پنجرهٔ مرجوعی) است و از {payout.AvailableAtUtc:yyyy-MM-dd HH:mm} (UTC) قابل پرداخت می‌شود.",
                        ErrorCodes.PayoutOnHold.Code);

                // M7.13 / Batch 44: بدهی‌های باز فروشنده (قدیمی‌ترین اول) از همین تسویه کسر می‌شود.
                var unsettledDebts = await _unitOfWork.Repository<SellerDebt>()
                    .ListAsync(new Spec<SellerDebt>()
                        .Where(d => d.SellerId == payout.SellerId && !d.IsSettled && d.RemainingAmount > 0)
                        .OrderBy(d => d.CreatedAtUtc)
                        .ThenBy(d => d.SellerDebtId)
                        .AsTracking(),
                        cancellationToken);

                var settlements = new List<(SellerDebt Debt, decimal Amount)>();
                var offset = 0m;
                foreach (var debt in unsettledDebts)
                {
                    var room = payout.Amount - offset;
                    if (room <= 0)
                        break;

                    var take = Math.Min(debt.RemainingAmount, room);
                    if (take <= 0)
                        continue;

                    settlements.Add((debt, take));
                    offset += take;
                }

                var net = payout.Amount - offset;
                if (expectedNetAmount.HasValue && expectedNetAmount.Value != net)
                    throw new ConflictException(
                        $"مبلغ خالص این تسویه {net} است، نه {expectedNetAmount.Value}؛ بدهی‌های فروشنده تغییر کرده است. فهرست را تازه کنید.",
                        ErrorCodes.PayoutNetChanged.Code);

                foreach (var (debt, amount) in settlements)
                {
                    debt.ApplySettlement(amount, now);
                    _unitOfWork.Repository<SellerDebt>().Update(debt);

                    if (_auditService != null)
                    {
                        await _auditService.RecordAsync(
                            "SellerDebt.Settled",
                            nameof(SellerDebt),
                            debt.SellerDebtId.ToString(),
                            $"seller={debt.SellerId} settled={amount} totalSettled={debt.SettledAmount} original={debt.OriginalAmount} payoutId={payout.SellerPayoutId}",
                            cancellationToken);
                    }
                }

                payout.MarkPaid(reference, offset, now);
                _unitOfWork.Repository<SellerPayout>().Update(payout);

                if (_auditService != null)
                {
                    await _auditService.RecordAsync(
                        "SellerPayout.Paid",
                        nameof(SellerPayout),
                        payout.SellerPayoutId.ToString(),
                        $"seller={payout.SellerId} amount={payout.Amount} debtOffset={payout.DebtOffsetAmount} net={payout.NetAmount} ref={reference}",
                        cancellationToken);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation(
                    "Payout {PayoutId} marked as paid with reference {Reference}: amount {Amount}, debt offset {Offset}, net {Net}",
                    payoutId, reference, payout.Amount, payout.DebtOffsetAmount, payout.NetAmount);
                return _mapper.Map<SellerPayoutDto>(payout);
            }
            catch (DbUpdateConcurrencyException)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw new ConflictException("تغییرات هم‌زمان رخ داده است. لطفاً دوباره تلاش کنید.", "CONCURRENCY_CONFLICT");
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }

        public async Task<SellerPayoutDto> CancelPayoutAsync(int payoutId, CancellationToken cancellationToken = default)
        {
            var payout = await _unitOfWork.Repository<SellerPayout>().GetByIdAsync(payoutId, cancellationToken);
            if (payout == null)
                throw new NotFoundException("تسویه یافت نشد.", "PAYOUT_NOT_FOUND");

            if (payout.Status != PayoutStatus.Pending)
                throw new ConflictException("فقط تسویه‌های در انتظار قابل لغو هستند.", "PAYOUT_INVALID_STATE");

            payout.Cancel();
            _unitOfWork.Repository<SellerPayout>().Update(payout);

            if (_auditService != null)
            {
                await _auditService.RecordAsync(
                    "SellerPayout.Cancelled",
                    nameof(SellerPayout),
                    payout.SellerPayoutId.ToString(),
                    $"seller={payout.SellerId} amount={payout.Amount}",
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payout {PayoutId} cancelled", payoutId);

            return _mapper.Map<SellerPayoutDto>(payout);
        }

        /// <summary>
        /// Batch 44 (F-09): Paid/Cancelled همان مقدار ثبت‌شده را نشان می‌دهند. برای Pending، بدهی باز هر فروشنده به ترتیب
        /// AvailableAtUtc (و سپس شناسه) روی تسویه‌های Pending او تقسیم می‌شود؛ جمع خالص‌ها = جمع ناخالص − بدهی باز
        /// (حداکثر تا جمع ناخالص). اگر ادمین به همین ترتیب بپردازد، خالص پیش‌نمایش با خالص ثبت‌شده یکی است؛ در هر صورت
        /// خالص هنگام پرداخت دوباره حساب می‌شود و ExpectedNetAmount جلوی واریزِ مبلغ نادرست را می‌گیرد.
        /// </summary>
        private async Task<IReadOnlyList<SellerPayoutDto>> ToDtosWithPreviewAsync(
            IReadOnlyList<SellerPayout> payouts,
            CancellationToken cancellationToken)
        {
            var dtos = payouts.Select(p => _mapper.Map<SellerPayoutDto>(p)).ToList();

            var pending = dtos.Where(d => d.Status == nameof(PayoutStatus.Pending)).ToList();
            if (pending.Count == 0)
                return dtos;

            var sellerIds = pending.Select(d => d.SellerId).Distinct().ToList();
            var openDebts = await _unitOfWork.Repository<SellerDebt>()
                .Query()
                .AsNoTracking()
                .Where(d => sellerIds.Contains(d.SellerId) && !d.IsSettled)
                .Select(d => new { d.SellerId, d.RemainingAmount })
                .ToListAsync(cancellationToken);

            // Batch 45: ستون صریح RemainingAmount (اصلاحیهٔ مالک ۲).
            var debtBySeller = openDebts
                .GroupBy(d => d.SellerId)
                .ToDictionary(g => g.Key, g => g.Sum(d => d.RemainingAmount));

            foreach (var group in pending.GroupBy(d => d.SellerId))
            {
                debtBySeller.TryGetValue(group.Key, out var remainingDebt);

                foreach (var dto in group.OrderBy(d => d.AvailableAtUtc).ThenBy(d => d.SellerPayoutId))
                {
                    var take = Math.Max(0m, Math.Min(remainingDebt, dto.Amount));
                    dto.DebtOffsetAmount = take;
                    dto.NetAmount = dto.Amount - take;
                    remainingDebt -= take;
                }
            }

            return dtos;
        }
    }
}
