using BusinessLogic.DTOs.Payout;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface ISellerPayoutService
    {
        Task<IEnumerable<SellerPayoutDto>> GetMyPayoutsAsync(CancellationToken cancellationToken = default);
        Task<IEnumerable<SellerPayoutDto>> GetAllPayoutsAsync(CancellationToken cancellationToken = default);
        Task<SellerPayoutDto> MarkAsPaidAsync(int payoutId, string? reference, CancellationToken cancellationToken = default);
        Task<SellerPayoutDto> CancelPayoutAsync(int payoutId, CancellationToken cancellationToken = default);
    }
}
