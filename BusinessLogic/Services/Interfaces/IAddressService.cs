using BusinessLogic.DTOs.Address;
using BusinessLogic.DTOs.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IAddressService
    {
        Task<AddressDto> CreateAsync(int userId, CreateAddressDto dto, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AddressDto>> GetAllAsync(int userId, CancellationToken cancellationToken = default);

        Task<AddressDto?> UpdateAsync(int userId, int addressId, UpdateAddressDto dto, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(int userId, int addressId, CancellationToken cancellationToken = default);
    }
}
