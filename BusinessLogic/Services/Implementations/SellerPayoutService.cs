using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Payout;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class SellerPayoutService : ISellerPayoutService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<SellerPayoutService> _logger;

        public SellerPayoutService(IUnitOfWork unitOfWork, ICurrentUserService currentUser, ILogger<SellerPayoutService> logger)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _logger = logger;
        }

        public async Task<IEnumerable<SellerPayoutDto>> GetMyPayoutsAsync(CancellationToken cancellationToken = default)
        {
            var userId = _currentUser.TryGetCurrentUserId();
            if (userId is null) return Enumerable.Empty<SellerPayoutDto>();

            var seller = (await _unitOfWork.Repository<Seller>()
                .ListAsync(new Application.Common.Specifications.Spec<Seller>().Where(s => s.UserId == userId.Value), cancellationToken))
                .FirstOrDefault();
            if (seller is null) return Enumerable.Empty<SellerPayoutDto>();

            var payouts = await _unitOfWork.Repository<SellerPayout>()
                .ListAsync(new Application.Common.Specifications.Spec<SellerPayout>().Where(p => p.SellerId == seller.SellerId), cancellationToken);

            return payouts.Select(Map).ToList();
        }

        public async Task<IEnumerable<SellerPayoutDto>> GetAllPayoutsAsync(CancellationToken cancellationToken = default)
        {
            var payouts = await _unitOfWork.Repository<SellerPayout>()
                .ListAsync(new Application.Common.Specifications.Spec<SellerPayout>(), cancellationToken);
            return payouts.Select(Map).ToList();
        }

        private static SellerPayoutDto Map(SellerPayout p) => new SellerPayoutDto
        {
            SellerPayoutId = p.SellerPayoutId,
            SellerId = p.SellerId,
            OrderSellerGroupId = p.OrderSellerGroupId,
            Amount = p.Amount,
            Status = p.Status.ToString(),
            PeriodStart = p.PeriodStart,
            PeriodEnd = p.PeriodEnd,
            Reference = p.Reference,
            PaidAt = p.PaidAt
        };
    }
}
