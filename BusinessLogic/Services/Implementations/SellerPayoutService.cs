using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Payout;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class SellerPayoutService : ISellerPayoutService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;
        private readonly ILogger<SellerPayoutService> _logger;

        public SellerPayoutService(IUnitOfWork unitOfWork, ICurrentUserService currentUser, IMapper mapper, ILogger<SellerPayoutService> logger)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _mapper = mapper;
            _logger = logger;
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
            var payout = await _unitOfWork.Repository<SellerPayout>().GetByIdAsync(payoutId, cancellationToken);
            if (payout == null)
                throw new NotFoundException("تسویه یافت نشد.", "PAYOUT_NOT_FOUND");

            try
            {
                payout.MarkPaid(reference);
            }
            catch (InvalidOperationException ex)
            {
                throw new BusinessException(ex.Message, "PAYOUT_INVALID_STATUS");
            }

            _unitOfWork.Repository<SellerPayout>().Update(payout);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payout {PayoutId} marked as paid with reference {Reference}", payoutId, reference);

            return _mapper.Map<SellerPayoutDto>(payout);
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payout {PayoutId} cancelled", payoutId);

            return _mapper.Map<SellerPayoutDto>(payout);
        }
    }
}
