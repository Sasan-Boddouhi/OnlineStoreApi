using Application.Common.Constants;
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

        // Batch 49 (F-29): فروشنده فقط آدرس خودش را به انبار وصل می‌کند؛
        // وگرنه آدرس کاربر دیگر به انبار او قفل می‌شد (FK از نوع Restrict) و قابل حذف نبود.
        if (!IsAdmin())
            EnsureAddressOwnedBy(address, currentUserId);

        var entity = _mapper.Map<Warehouse>(dto);
        entity.SellerId = null;

        if (dto.SellerId.HasValue)
        {
            var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(dto.SellerId.Value, cancellationToken);
            if (seller == null)
                throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");
            entity.SellerId = dto.SellerId.Value;
        }
        else
        {
            var seller = await _unitOfWork.Repository<Seller>()
                .FirstOrDefaultAsync(new Spec<Seller>().Where(s => s.UserId == currentUserId), cancellationToken);
            if (seller != null)
                entity.SellerId = seller.SellerId;
        }

        if (entity.SellerId is null)
            await EnsureNoOtherPlatformWarehouseAsync(null, cancellationToken);

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

        var isAdmin = IsAdmin();

        if (!isAdmin)
        {
            var seller = await _unitOfWork.Repository<Seller>()
                .FirstOrDefaultAsync(new Spec<Seller>().Where(s => s.UserId == userId), cancellationToken);
            if (seller == null || entity.SellerId != seller.SellerId)
                throw new ForbiddenException("WAREHOUSE_FORBIDDEN");
        }
        else if (dto.SellerId != entity.SellerId)
        {
            if (dto.SellerId.HasValue)
            {
                var seller = await _unitOfWork.Repository<Seller>().GetByIdAsync(dto.SellerId.Value, cancellationToken);
                if (seller == null)
                    throw new NotFoundException("فروشنده یافت نشد.", "SELLER_NOT_FOUND");
                entity.SellerId = dto.SellerId.Value;
            }
            else
            {
                await EnsureNoOtherPlatformWarehouseAsync(entity.WarehouseId, cancellationToken);
                entity.SellerId = null;
            }
        }

        var address = await _unitOfWork.Repository<Address>().GetByIdAsync(dto.AddressId, cancellationToken);
        if (address == null)
            throw new NotFoundException("آدرس یافت نشد.", "ADDRESS_NOT_FOUND");

        // Batch 49 (F-29): فقط ارجاع «تازه» بررسی می‌شود؛ آدرسی که مدیر قبلاً برای این انبار
        // گذاشته، ویرایش نام توسط فروشنده را قفل نمی‌کند.
        if (!isAdmin && dto.AddressId != entity.AddressId)
            EnsureAddressOwnedBy(address, userId);

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

        if (!IsAdmin())
        {
            var seller = await _unitOfWork.Repository<Seller>()
                .FirstOrDefaultAsync(new Spec<Seller>().Where(s => s.UserId == currentUserId), cancellationToken);
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

    /// <summary>
    /// Batch 44: حداکثر یک انبار پلتفرم (SellerId = null) مجاز است (ایندکس یکتای UX_Warehouse_SinglePlatform).
    /// پیش از ذخیره بررسی می‌شود تا پاسخ 409 با کد روشن باشد؛ ایندکس سد نهایی درخواست‌های هم‌زمان است.
    /// </summary>
    private async Task EnsureNoOtherPlatformWarehouseAsync(int? exceptWarehouseId, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Repository<Warehouse>().AnyAsync(
            w => w.SellerId == null && (exceptWarehouseId == null || w.WarehouseId != exceptWarehouseId.Value),
            cancellationToken);

        if (exists)
            throw new ConflictException(
                "انبار پلتفرم از قبل وجود دارد؛ فقط یک انبار پلتفرم مجاز است. انبار تازه را به یک فروشنده اختصاص دهید.",
                "PLATFORM_WAREHOUSE_EXISTS");
    }

    /// <summary>مدیر یا مدیر ارشد، همان قاعده‌ای که ویرایش و حذف انبار از قبل داشتند.</summary>
    private bool IsAdmin()
    {
        var role = _currentUserService.GetCurrentUserRole();
        return string.Equals(role, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, RoleConstants.SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Batch 49 (F-29): آدرس انبار باید متعلق به کاربر فراخواننده باشد.</summary>
    private static void EnsureAddressOwnedBy(Address address, int userId)
    {
        if (address.UserId != userId)
            throw new ForbiddenException("این آدرس متعلق به شما نیست و نمی‌توان آن را به انبار وصل کرد.", "ADDRESS_FORBIDDEN");
    }
}