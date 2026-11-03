using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BusinessLogic.DTOs.Shipping;

namespace BusinessLogic.Services.Interfaces;

public interface IShippingMethodService
{
    Task<IReadOnlyList<ShippingMethodDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ShippingMethodDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ShippingMethodDto> CreateAsync(CreateShippingMethodDto dto, CancellationToken cancellationToken = default);
    Task<ShippingMethodDto?> UpdateAsync(int id, UpdateShippingMethodDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
