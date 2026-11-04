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
    public sealed class SellerPayoutService : ISellerPayoutService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;
        private readonly ILogger<SellerPayoutService> _logger;
        private readonly IAuditService? _auditService;

        public SellerPayoutService(IUnitOfWork unitOfWork, ICurrentUserService currentUser, IMapper mapper, ILogger<SellerPayoutService> logger, IAuditService? auditService = null)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _mapper = mapper;
            _logger = logger;
            _auditService = auditService;
        }

        public async Task<IEnumerable<SellerPayoutDto>> GetMyPayoutsAsync(CancellationToken cancellationToken = default)
        {
            var userId = _currentUser.TryGetCurrentUserId();
            if (userId is null) return Enumerable.Empty<SellerPayoutDto>();

            var seller = await _unitOfWork.Repository<Seller>()
                .FirstOrDefaultAsync(new Application.Common.Specifications.Spec<Seller>().Where(s => s.UserId == userId.Value), cancellationToken);
            if (seller is null) return Enumerable.Empty<SellerPayoutDto>();

            var payouts = await _unitOfWork.Repository<SellerPayout>()
                .ListAsync(new Application.Common.Specifications.Spec<SellerPayout>().Where(p => p.SellerId == seller.SellerId), cancellationToken);

            return _mapper.Map<IEnumerable<SellerPayoutDto>>(payouts);
        }

        public async Task<IEnumerable<SellerPayoutDto>> GetAllPayoutsAsync(CancellationToken cancellationToken = default)
        {
            var payouts = await _unitOfWork.Repository<SellerPayout>()
                .ListAsync(new Application.Common.Specifications.Spec<SellerPayout>(), cancellationToken);
            return _mapper.Map<IEnumerable<SellerPayoutDto>>(payouts);
        }

        public async Task<SellerPayoutDto> MarkAsPaidAsync(int payoutId, string? reference, CancellationToken cancellationToken = default)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var payout = await _unitOfWork.Repository<SellerPayout>().GetByIdAsync(payoutId, cancellationToken);
                if (payout == null)
                    throw new NotFoundException("تسویه یافت نشد.", "PAYOUT_NOT_FOUND");

                if (payout.Status != PayoutStatus.Pending)
                    throw new BusinessException("فقط تسویه‌های در انتظار قابل پرداخت هستند.", "PAYOUT_INVALID_STATUS");

                // M7.13: Settle outstanding seller debts against this payout
                var unsettledDebts = await _unitOfWork.Repository<SellerDebt>()
                    .ListAsync(new Application.Common.Specifications.Spec<SellerDebt>()
                        .Where(d => d.SellerId == payout.SellerId && !d.IsSettled)
                        .OrderBy(d => d.CreatedAtUtc)
                        .AsTracking(),
                        cancellationToken);

                decimal remainingPayoutAmount = payout.Amount;
                foreach (var debt in unsettledDebts)
                {
                    if (remainingPayoutAmount <= 0)
                        break;

                    if (debt.Amount <= remainingPayoutAmount)
                    {
                        remainingPayoutAmount -= debt.Amount;
                        debt.IsSettled = true;
                        _unitOfWork.Repository<SellerDebt>().Update(debt);
                    }
                    else
                    {
                        debt.Amount -= remainingPayoutAmount;
                        remainingPayoutAmount = 0;
                        _unitOfWork.Repository<SellerDebt>().Update(debt);
                        break;
                    }
                }

                payout.MarkPaid(reference);
                _unitOfWork.Repository<SellerPayout>().Update(payout);

                if (_auditService != null)
                {
                    await _auditService.RecordAsync(
                        "SellerPayout.Paid",
                        nameof(SellerPayout),
                        payout.SellerPayoutId.ToString(),
                        $"seller={payout.SellerId} amount={payout.Amount} netRemaining={remainingPayoutAmount} ref={reference}",
                        cancellationToken);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _logger.LogInformation("Payout {PayoutId} marked as paid with reference {Reference}", payoutId, reference);
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

            try
            {
                payout.Cancel();
            }
            catch (InvalidOperationException ex)
            {
                throw new BusinessException(ex.Message, "PAYOUT_INVALID_STATUS");
            }

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
    }
}
