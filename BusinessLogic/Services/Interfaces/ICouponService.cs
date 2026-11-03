using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessLogic.DTOs.Coupon;

namespace BusinessLogic.Services.Interfaces;

public interface ICouponService
{
    Task<IReadOnlyList<CouponDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CouponDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<CouponDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<CouponDto> CreateAsync(CreateCouponDto dto, CancellationToken cancellationToken = default);
    Task<CouponDto?> UpdateAsync(int id, UpdateCouponDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
