using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Warehouse;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;
public sealed class WarehouseService : IWarehouseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<WarehouseService> _logger;
    private readonly ICurrentUserService _currentUserService;

    public WarehouseService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<WarehouseService> logger,
        ICurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<WarehouseDto> CreateAsync(CreateWarehouseDto dto, int currentUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating warehouse: {WarehouseName}", dto.Name);

        var address = await _unitOfWork.Repository<Address>().GetByIdAsync(dto.AddressId, cancellationToken);
        if (address == null)
            throw new NotFoundException("آدرس یافت نشد.", "ADDRESS_NOT_FOUND");

        var entity = _mapper.Map<Warehouse>(dto);
        entity.SellerId = null;

        var role = _currentUserService.GetCurrentUserRole();
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.SellerId.HasValue)
            {
                var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(dto.SellerId.Value, cancellationToken);
                if (seller == null)
                    throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");
                entity.SellerId = dto.SellerId.Value;
            }
        }
        else
        {
            var seller = (await _unitOfWork.Repository<Seller>()
                .ListAsync(new Spec<Seller>().Where(s => s.UserId == currentUserId), cancellationToken))
                .FirstOrDefault();
            if (seller != null)
                entity.SellerId = seller.SellerId;
        }

        await _unitOfWork.Repository<Warehouse>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Warehouse created with ID: {WarehouseId}", entity.WarehouseId);

        return await GetByIdAsync(entity.WarehouseId, cancellationToken) ?? _mapper.Map<WarehouseDto>(entity);
    }

    public async Task<WarehouseDto?> GetByIdAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId, cancellationToken);
        if (entity == null)
            return null;

        var dto = _mapper.Map<WarehouseDto>(entity);
        dto.AddressName = entity.Address != null ? entity.Address.Plaque + " " + entity.Address.Unit : "";
        dto.SellerName = entity.Seller != null ? entity.Seller.StoreName : "";
        
        return dto;
    }

    public async Task<IReadOnlyList<WarehouseDto>> GetAllAsync(int? sellerId = null, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<Warehouse>()
            .Include(w => w.Address)
            .Include(w => w.Seller);

        if (sellerId.HasValue)
        {
            spec.Where(w => w.SellerId == sellerId.Value);
        }

        var entities = await _unitOfWork.Repository<Warehouse>().ListAsync(spec, cancellationToken);
        
        var dtos = entities.Select(e =>
        {
            var dto = _mapper.Map<WarehouseDto>(e);
            dto.AddressName = e.Address != null ? e.Address.Plaque + " " + e.Address.Unit : "";
            dto.SellerName = e.Seller != null ? e.Seller.StoreName : "";
            return dto;
        }).ToList();

        return dtos;
    }

    public async Task<WarehouseDto?> UpdateAsync(int userId, int warehouseId, UpdateWarehouseDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating warehouse {WarehouseId}", warehouseId);

        var entity = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId, cancellationToken);
        if (entity == null)
            return null;

        var role = _currentUserService.GetCurrentUserRole();
        var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
        {
            var seller = (await _unitOfWork.Repository<Seller>()
                .ListAsync(new Spec<Seller>().Where(s => s.UserId == userId), cancellationToken))
                .FirstOrDefault();
            if (seller == null || entity.SellerId != seller.SellerId)
                throw new ForbiddenException("WAREHOUSE_FORBIDDEN");
        }

        var address = await _unitOfWork.Repository<Address>().GetByIdAsync(dto.AddressId, cancellationToken);
        if (address == null)
            throw new NotFoundException("آدرس یافت نشد.", "ADDRESS_NOT_FOUND");

        if (dto.SellerId.HasValue)
        {
            var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(dto.SellerId.Value, cancellationToken);
            if (seller == null)
                throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");
        }

        _mapper.Map(dto, entity);
        _unitOfWork.Repository<Warehouse>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Warehouse {WarehouseId} updated", warehouseId);

        return await GetByIdAsync(warehouseId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(int warehouseId, int currentUserId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting warehouse {WarehouseId}", warehouseId);

        var entity = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId, cancellationToken);
        if (entity == null)
            return false;

        var role = _currentUserService.GetCurrentUserRole();
        var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
        {
            var seller = (await _unitOfWork.Repository<Seller>()
                .ListAsync(new Spec<Seller>().Where(s => s.UserId == currentUserId), cancellationToken))
                .FirstOrDefault();
            if (seller == null || entity.SellerId != seller.SellerId)
                throw new ForbiddenException("WAREHOUSE_FORBIDDEN");
        }

        var hasInventories = await _unitOfWork.Repository<Inventory>()
            .AnyAsync(i => i.WarehouseId == warehouseId, cancellationToken);

        if (hasInventories)
            throw new BusinessException("این انبار موجودی دارد و قابل حذف نیست.", "WAREHOUSE_HAS_INVENTORIES");

        _unitOfWork.Repository<Warehouse>().Delete(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Warehouse {WarehouseId} deleted", warehouseId);

        return true;
    }
}