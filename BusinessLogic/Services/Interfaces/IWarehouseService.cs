using Application.Entities;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.Warehouse;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IWarehouseService
    {
        Task<WarehouseDto> CreateAsync(CreateWarehouseDto dto, int currentUserId, CancellationToken cancellationToken = default);
        Task<WarehouseDto?> GetByIdAsync(int warehouseId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<WarehouseDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<WarehouseDto?> UpdateAsync(int userId, int warehouseId, UpdateWarehouseDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int warehouseId, int currentUserId, CancellationToken cancellationToken = default);
    }
}